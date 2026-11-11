using Microsoft.AspNetCore.Mvc.Testing;
using WorkBoard.Api.Tests.Fakes;

namespace WorkBoard.Api.Tests;

public class StatusRulesTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static async Task<string> StatusOf(TestTenant t, Guid issue) =>
        (await t.GetAsync($"/api/issues/{issue}")).GetProperty("status").GetString()!;

    private static Task SetDraft(TestTenant t, Guid pr, bool draft) =>
        t.ExecAsync("update pull_requests set draft = @draft where id = @pr", new { pr, draft });

    private static Task SetReason(TestTenant t, Guid issue, string reason) =>
        t.ExecAsync("update issues set state_reason = @reason where id = @issue", new { issue, reason });

    [DbFact]
    public async Task Rule1_closed_as_not_planned_is_cancelled_with_category_done_even_with_override()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var issue = await t.AddIssueAsync(repo, closed: true);
        await t.BoardAsync(issue, statusOverride: "in_progress");
        await SetReason(t, issue, "not_planned");

        var detail = await t.GetAsync($"/api/issues/{issue}");

        Assert.Equal("cancelled", detail.GetProperty("status").GetString());
        Assert.Equal("done", detail.GetProperty("category").GetString());
    }

    [DbFact]
    public async Task Rule2_closed_is_done_ahead_of_override_and_open_pull_request()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var issue = await t.AddIssueAsync(repo, closed: true);
        await SetReason(t, issue, "completed");
        await t.BoardAsync(issue, statusOverride: "todo");
        await t.AddPullRequestAsync(repo, issue);

        Assert.Equal("done", await StatusOf(t, issue));
    }

    [DbFact]
    public async Task Rule3_override_beats_linked_pull_requests_on_an_open_issue()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var issue = await t.AddIssueAsync(repo);
        await t.BoardAsync(issue, statusOverride: "todo");
        await t.AddPullRequestAsync(repo, issue, state: "merged");

        Assert.Equal("todo", await StatusOf(t, issue));
    }

    [DbFact]
    public async Task Rule4_merged_pull_request_on_an_open_issue_is_in_review()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var issue = await t.AddIssueAsync(repo);
        var pr = await t.AddPullRequestAsync(repo, issue, state: "merged");
        await SetDraft(t, pr, true);

        Assert.Equal("in_review", await StatusOf(t, issue));
    }

    [DbFact]
    public async Task Rule5_open_pull_request_that_is_not_draft_is_in_review_without_review_request()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var issue = await t.AddIssueAsync(repo);
        await t.AddPullRequestAsync(repo, issue, review: false);

        Assert.Equal("in_review", await StatusOf(t, issue));
    }

    [DbFact]
    public async Task Rule6_open_draft_pull_request_is_in_progress()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var issue = await t.AddIssueAsync(repo);
        var pr = await t.AddPullRequestAsync(repo, issue);
        await SetDraft(t, pr, true);

        var detail = await t.GetAsync($"/api/issues/{issue}");

        Assert.Equal("in_progress", detail.GetProperty("status").GetString());
        Assert.Equal("in_progress", detail.GetProperty("category").GetString());
    }

    [DbFact]
    public async Task Rule7_no_signals_is_todo_and_a_closed_unmerged_pull_request_is_ignored()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var bare = await t.AddIssueAsync(repo);
        var withClosedPr = await t.AddIssueAsync(repo);
        await t.AddPullRequestAsync(repo, withClosedPr, state: "closed");

        Assert.Equal("todo", await StatusOf(t, bare));
        Assert.Equal("todo", await StatusOf(t, withClosedPr));
    }

    [DbFact]
    public async Task Cancelled_issues_are_excluded_from_progress_counts_but_filterable()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var project = await t.AddProjectAsync("p", repo);
        var sprint = await t.AddSprintAsync("s");
        var done = await t.AddIssueAsync(repo, closed: true);
        var cancelled = await t.AddIssueAsync(repo, closed: true);
        await SetReason(t, cancelled, "not_planned");
        await t.BoardAsync(done, sprint: sprint);
        await t.BoardAsync(cancelled, sprint: sprint, priority: "urgent");

        var overview = await t.OverviewAsync();
        var by = overview.GetProperty("projects").EnumerateArray()
            .Single(p => p.GetProperty("project").GetProperty("id").GetString() == project.ToString())
            .GetProperty("byStatus");

        Assert.Equal(1, by.GetProperty("done").GetInt32());
        Assert.Equal(1, overview.GetProperty("sprint").GetProperty("byStatus").GetProperty("done").GetInt32());
        Assert.Equal(1, overview.GetProperty("counts").GetProperty("doneThisWeek").GetInt32());
        Assert.Empty(overview.GetProperty("attention").GetProperty("urgent").EnumerateArray());

        var onlyCancelled = await t.GetAsync("/api/issues?status=cancelled&scope=all");
        Assert.Equal([cancelled.ToString()], onlyCancelled.EnumerateArray().Select(i => i.GetProperty("id").GetString()));
        var weekDone = await t.GetAsync("/api/issues?doneSince=week");
        Assert.Equal([done.ToString()], weekDone.EnumerateArray().Select(i => i.GetProperty("id").GetString()));
    }
}
