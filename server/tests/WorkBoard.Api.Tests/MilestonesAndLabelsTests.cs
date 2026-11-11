using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WorkBoard.Api.Tests.Fakes;

namespace WorkBoard.Api.Tests;

public class MilestonesAndLabelsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static async Task<Guid> AddMilestoneAsync(
        TestTenant t, Guid repo, int number, string title, string state = "open", DateTime? dueOn = null)
    {
        var id = Guid.NewGuid();
        await t.ExecAsync("""
            insert into milestones (id, tenant_id, repository_id, github_milestone_id, number, title, state, due_on, html_url)
            values (@id, @tid, @repo, @gid, @number, @title, @state, @dueOn, @url)
            """, new
        {
            id, tid = t.Id, repo, gid = TestTenant.NextId(), number, title, state, dueOn,
            url = $"https://github.com/o/r/milestone/{number}",
        });
        return id;
    }

    private static async Task<Guid> AddLabelAsync(TestTenant t, Guid repo, string name, string? color = "d73a4a")
    {
        var id = Guid.NewGuid();
        await t.ExecAsync(
            "insert into labels (id, tenant_id, repository_id, github_label_id, name, color) values (@id, @tid, @repo, @gid, @name, @color)",
            new { id, tid = t.Id, repo, gid = TestTenant.NextId(), name, color });
        return id;
    }

    private static Task Tag(TestTenant t, Guid issue, Guid label) =>
        t.ExecAsync("insert into issue_labels (tenant_id, issue_id, label_id) values (@tid, @issue, @label)",
            new { tid = t.Id, issue, label });

    [DbFact]
    public async Task Issue_carries_html_url_labels_sorted_by_name_and_milestone()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var due = new DateTime(2030, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var milestone = await AddMilestoneAsync(t, repo, 1, "M1", dueOn: due);
        var issue = await t.AddIssueAsync(repo);
        var bare = await t.AddIssueAsync(repo);
        await t.ExecAsync("update issues set html_url = 'https://github.com/o/r/issues/1', milestone_id = @milestone where id = @issue",
            new { milestone, issue });
        await Tag(t, issue, await AddLabelAsync(t, repo, "zeta", null));
        await Tag(t, issue, await AddLabelAsync(t, repo, "alpha", "0e8a16"));

        var list = await t.GetAsync("/api/issues");
        var got = list.EnumerateArray().Single(i => i.GetProperty("id").GetString() == issue.ToString());
        var empty = list.EnumerateArray().Single(i => i.GetProperty("id").GetString() == bare.ToString());

        Assert.Equal("https://github.com/o/r/issues/1", got.GetProperty("htmlUrl").GetString());
        var labels = got.GetProperty("labels").EnumerateArray().ToList();
        Assert.Equal(["alpha", "zeta"], labels.Select(l => l.GetProperty("name").GetString()));
        Assert.Equal("0e8a16", labels[0].GetProperty("color").GetString());
        Assert.Equal(JsonValueKind.Null, labels[1].GetProperty("color").ValueKind);
        var ms = got.GetProperty("milestone");
        Assert.Equal(milestone.ToString(), ms.GetProperty("id").GetString());
        Assert.Equal("M1", ms.GetProperty("title").GetString());
        Assert.Equal(due, ms.GetProperty("dueOn").GetDateTime().ToUniversalTime());

        Assert.Equal(JsonValueKind.Null, empty.GetProperty("htmlUrl").ValueKind);
        Assert.Empty(empty.GetProperty("labels").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("milestone").ValueKind);
    }

    [DbFact]
    public async Task Issue_detail_has_the_same_fields_and_pull_request_html_url()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var issue = await t.AddIssueAsync(repo);
        await Tag(t, issue, await AddLabelAsync(t, repo, "bug"));
        var pr = await t.AddPullRequestAsync(repo, issue);
        await t.ExecAsync("update pull_requests set html_url = 'https://github.com/o/r/pull/9' where id = @pr", new { pr });

        var detail = await t.GetAsync($"/api/issues/{issue}");

        Assert.Equal("bug", detail.GetProperty("labels")[0].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("milestone").ValueKind);
        Assert.Equal("https://github.com/o/r/pull/9", detail.GetProperty("pullRequests")[0].GetProperty("htmlUrl").GetString());
    }

    [DbFact]
    public async Task Milestones_list_open_first_then_by_due_date_with_counts_that_skip_cancelled()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var late = await AddMilestoneAsync(t, repo, 1, "late", dueOn: DateTime.UtcNow.AddDays(30));
        var soon = await AddMilestoneAsync(t, repo, 2, "soon", dueOn: DateTime.UtcNow.AddDays(3));
        var undated = await AddMilestoneAsync(t, repo, 3, "undated");
        await AddMilestoneAsync(t, repo, 4, "shipped", "closed", DateTime.UtcNow.AddDays(-5));

        var todo = await t.AddIssueAsync(repo);
        var done = await t.AddIssueAsync(repo, closed: true);
        var cancelled = await t.AddIssueAsync(repo, closed: true);
        await t.ExecAsync("update issues set state_reason = 'not_planned' where id = @cancelled", new { cancelled });
        foreach (var id in new[] { todo, done, cancelled })
            await t.ExecAsync("update issues set milestone_id = @soon where id = @id", new { soon, id });

        var result = await t.GetAsync("/api/milestones");
        var list = result.EnumerateArray().ToList();

        Assert.Equal(["soon", "late", "undated", "shipped"], list.Select(m => m.GetProperty("title").GetString()));
        var first = list[0];
        Assert.Equal(soon.ToString(), first.GetProperty("id").GetString());
        Assert.Equal("r", first.GetProperty("repository").GetProperty("name").GetString());
        Assert.Equal(repo.ToString(), first.GetProperty("repository").GetProperty("id").GetString());
        Assert.Equal(2, first.GetProperty("number").GetInt32());
        Assert.Equal("open", first.GetProperty("state").GetString());
        Assert.Equal("https://github.com/o/r/milestone/2", first.GetProperty("htmlUrl").GetString());
        Assert.Equal(2, first.GetProperty("issueCount").GetInt32());
        Assert.Equal(1, first.GetProperty("doneCount").GetInt32());
        Assert.Equal(0, list[1].GetProperty("issueCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, list[2].GetProperty("dueOn").ValueKind);
        Assert.Equal("closed", list[3].GetProperty("state").GetString());
        _ = (late, undated);
    }

    [DbFact]
    public async Task Milestones_never_leak_across_tenants()
    {
        await using var a = await TestTenant.CreateAsync(factory);
        await using var b = await TestTenant.CreateAsync(factory);
        await AddMilestoneAsync(a, await a.AddRepoAsync("ra"), 1, "only-a");
        await AddMilestoneAsync(b, await b.AddRepoAsync("rb"), 1, "only-b");

        Assert.Equal(["only-a"], (await a.GetAsync("/api/milestones")).EnumerateArray().Select(m => m.GetProperty("title").GetString()));
        Assert.Equal(["only-b"], (await b.GetAsync("/api/milestones")).EnumerateArray().Select(m => m.GetProperty("title").GetString()));
    }
}
