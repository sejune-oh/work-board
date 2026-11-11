using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WorkBoard.Api.Tests.Fakes;

namespace WorkBoard.Api.Tests;

public class SprintsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(
        TestTenant t, HttpMethod method, string url, object? body = null, string? key = null)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Dev-User", t.ViewerLogin);
        if (key is not null)
            request.Headers.Add("Idempotency-Key", key);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (text.Length > 0 && text[0] != '{' && text[0] != '[')
            throw new InvalidOperationException($"{(int)response.StatusCode} {text}");
        return (response.StatusCode, text.Length == 0 ? default : JsonSerializer.Deserialize<JsonElement>(text));
    }

    private static string Today(int offset) => DateTime.UtcNow.AddHours(9).Date.AddDays(offset).ToString("yyyy-MM-dd");

    private static Guid Id(JsonElement e, params string[] path) =>
        Guid.Parse(path.Aggregate(e, (x, p) => x.GetProperty(p)).GetString()!);

    private static async Task<List<string>> EventsAsync(TestTenant t, Guid issue) =>
        (await t.QueryAsync("select kind, from_value, to_value from issue_events where issue_id = @issue order by at, kind desc", new { issue }))
            .Select(r => $"{r.kind}:{r.from_value}>{r.to_value}").ToList();

    [DbFact]
    public async Task Create_list_and_patch_with_concurrency_and_unique_names()
    {
        await using var t = await TestTenant.CreateAsync(factory);

        var (status, created) = await SendAsync(t, HttpMethod.Post, "/api/sprints",
            new { name = "S1", startDate = Today(-1), endDate = Today(5) });
        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("active", created.GetProperty("state").GetString());
        var id = Id(created, "id");
        await SendAsync(t, HttpMethod.Post, "/api/sprints", new { name = "Next", startDate = Today(10), endDate = Today(20) });
        await t.ExecAsync("insert into sprints (tenant_id, name, start_date, end_date) values (@tid, 'Old', current_date - 40, current_date - 30)", new { tid = t.Id });

        var dup = await SendAsync(t, HttpMethod.Post, "/api/sprints", new { name = " s1 ", startDate = Today(0), endDate = Today(1) });
        Assert.Equal(HttpStatusCode.Conflict, dup.Status);
        Assert.Equal("name_taken", dup.Body.GetProperty("error").GetString());
        var bad = await SendAsync(t, HttpMethod.Post, "/api/sprints", new { name = "x", startDate = Today(3), endDate = Today(1) });
        Assert.Equal("invalid_dates", bad.Body.GetProperty("error").GetString());

        var (_, list) = await SendAsync(t, HttpMethod.Get, "/api/sprints");
        Assert.Equal(["S1"], list.GetProperty("current").EnumerateArray().Select(x => x.GetProperty("name").GetString()));
        Assert.Equal(["Next"], list.GetProperty("upcoming").EnumerateArray().Select(x => x.GetProperty("name").GetString()));
        Assert.Equal(["Old"], list.GetProperty("past").EnumerateArray().Select(x => x.GetProperty("name").GetString()));

        var updatedAt = created.GetProperty("updatedAt").GetString();
        var (patchStatus, patched) = await SendAsync(t, HttpMethod.Patch, $"/api/sprints/{id}", new { name = "Renamed", updatedAt });
        Assert.Equal(HttpStatusCode.OK, patchStatus);
        Assert.Equal("Renamed", patched.GetProperty("name").GetString());
        var stale = await SendAsync(t, HttpMethod.Patch, $"/api/sprints/{id}", new { name = "Again", updatedAt });
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);
        Assert.Equal("conflict", stale.Body.GetProperty("error").GetString());
        var missing = await SendAsync(t, HttpMethod.Patch, $"/api/sprints/{id}", new { name = "Again" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.Status);
        Assert.Equal(2, await t.ScalarAsync<int>("select count(*)::int from audit_log where tenant_id = @tid and target_id = @id", new { tid = t.Id, id }));
    }

    [DbFact]
    public async Task Bulk_add_move_and_remove_record_events_and_audit()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var a = await t.AddIssueAsync(repo);
        var b = await t.AddIssueAsync(repo);
        var c = await t.AddIssueAsync(repo);
        var s1 = await t.AddSprintAsync("One");
        var s2 = await t.AddSprintAsync("Two");
        await t.BoardAsync(c, priority: "high", sprint: s2);

        var (status, added) = await SendAsync(t, HttpMethod.Post, $"/api/sprints/{s1}/issues", new { issueIds = new[] { a, b, c, a } });
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((2, 1, 0), (added.GetProperty("added").GetInt32(), added.GetProperty("moved").GetInt32(), added.GetProperty("unchanged").GetInt32()));
        Assert.Equal(3, added.GetProperty("sprint").GetProperty("issueCount").GetInt32());
        Assert.Equal(["sprint_added:>One"], await EventsAsync(t, a));
        Assert.Equal(["sprint_removed:Two>", "sprint_added:>One"], await EventsAsync(t, c));
        Assert.Equal("high", await t.ScalarAsync<string>("select priority from board_fields where issue_id = @c", new { c }));

        var again = await SendAsync(t, HttpMethod.Post, $"/api/sprints/{s1}/issues", new { issueIds = new[] { a } });
        Assert.Equal(1, again.Body.GetProperty("unchanged").GetInt32());
        Assert.Single(await EventsAsync(t, a));

        var removed = await SendAsync(t, HttpMethod.Delete, $"/api/sprints/{s1}/issues", new { issueIds = new[] { a, b } });
        Assert.Equal(2, removed.Body.GetProperty("removed").GetInt32());
        Assert.Equal(["sprint_added:>One", "sprint_removed:One>"], await EventsAsync(t, a));
        Assert.Null(await t.ScalarAsync<Guid?>("select sprint_id from board_fields where issue_id = @a", new { a }));
        Assert.Equal(1, await t.ScalarAsync<int>("select count(*)::int from board_fields where sprint_id = @s1", new { s1 }));
        Assert.Equal(3, await t.ScalarAsync<int>("select count(*)::int from audit_log where tenant_id = @tid and action like 'sprint.issues_%'", new { tid = t.Id }));

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(t, HttpMethod.Post, $"/api/sprints/{s1}/issues", new { issueIds = Array.Empty<Guid>() })).Status);
    }

    [DbFact]
    public async Task Other_tenants_sprints_and_issues_are_not_reachable()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        await using var other = await TestTenant.CreateAsync(factory);
        var mine = await t.AddSprintAsync("Mine");
        var theirs = await other.AddSprintAsync("Theirs");
        var myIssue = await t.AddIssueAsync(await t.AddRepoAsync("r"));
        var theirIssue = await other.AddIssueAsync(await other.AddRepoAsync("r"));

        var (_, list) = await SendAsync(t, HttpMethod.Get, "/api/sprints");
        Assert.DoesNotContain(list.GetProperty("current").EnumerateArray(), x => Id(x, "id") == theirs);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(t, HttpMethod.Post, $"/api/sprints/{theirs}/issues", new { issueIds = new[] { myIssue } })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(t, HttpMethod.Post, $"/api/sprints/{mine}/carry-over", new { toSprintId = theirs })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(t, HttpMethod.Patch, $"/api/sprints/{theirs}", new { name = "x", updatedAt = DateTime.UtcNow })).Status);

        var mixed = await SendAsync(t, HttpMethod.Post, $"/api/sprints/{mine}/issues", new { issueIds = new[] { myIssue, theirIssue } });
        Assert.Equal(HttpStatusCode.NotFound, mixed.Status);
        Assert.Equal(0, await t.ScalarAsync<int>("select count(*)::int from board_fields where sprint_id = @mine", new { mine }));
        Assert.Equal(0, await other.ScalarAsync<int>("select count(*)::int from board_fields where issue_id = @theirIssue", new { theirIssue }));
    }

    [DbFact]
    public async Task Carry_over_moves_unfinished_issues_and_can_be_undone_once()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var todo = await t.AddIssueAsync(repo);
        var doing = await t.AddIssueAsync(repo);
        var done = await t.AddIssueAsync(repo, closed: true);
        var cancelled = await t.AddIssueAsync(repo, closed: true);
        await t.ExecAsync("update issues set state_reason = 'not_planned' where id = @cancelled", new { cancelled });
        var s1 = await t.AddSprintAsync("One");
        var s2 = await t.AddSprintAsync("Two", "planned");
        foreach (var i in new[] { todo, done, cancelled })
            await t.BoardAsync(i, sprint: s1);
        await t.BoardAsync(doing, statusOverride: "in_progress", sprint: s1);

        var (status, result) = await SendAsync(t, HttpMethod.Post, $"/api/sprints/{s1}/carry-over", new { toSprintId = s2 });
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, result.GetProperty("movedCount").GetInt32());
        var carryId = Id(result, "carryOverId");
        Assert.Equal(s2, await t.ScalarAsync<Guid>("select sprint_id from board_fields where issue_id = @todo", new { todo }));
        Assert.Equal(s1, await t.ScalarAsync<Guid>("select sprint_id from board_fields where issue_id = @done", new { done }));
        Assert.Equal(["carried_over:One>Two"], await EventsAsync(t, doing));
        Assert.Empty(await EventsAsync(t, done));
        var member = await t.ScalarAsync<Guid>("select member_id from sprint_carry_overs where id = @carryId", new { carryId });
        Assert.Equal(await t.ScalarAsync<Guid>("select id from members where tenant_id = @tid limit 1", new { tid = t.Id }), member);

        var undo = await SendAsync(t, HttpMethod.Post, $"/api/sprints/{s1}/carry-over/{carryId}/undo");
        Assert.Equal(HttpStatusCode.OK, undo.Status);
        Assert.Equal(s1, await t.ScalarAsync<Guid>("select sprint_id from board_fields where issue_id = @todo", new { todo }));
        Assert.Equal(["carried_over:One>Two", "carried_over:Two>One"], await EventsAsync(t, todo));
        Assert.Equal("already_undone", (await SendAsync(t, HttpMethod.Post, $"/api/sprints/{s1}/carry-over/{carryId}/undo")).Body.GetProperty("error").GetString());
        Assert.Equal(2, await t.ScalarAsync<int>("select count(*)::int from audit_log where tenant_id = @tid and action like 'sprint.carry_over%'", new { tid = t.Id }));

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(t, HttpMethod.Post, $"/api/sprints/{s1}/carry-over", new { toSprintId = s1 })).Status);
    }

    [DbFact]
    public async Task Undo_is_refused_when_an_issue_moved_since()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var a = await t.AddIssueAsync(repo);
        var b = await t.AddIssueAsync(repo);
        var s1 = await t.AddSprintAsync("One");
        var s2 = await t.AddSprintAsync("Two");
        var s3 = await t.AddSprintAsync("Three");
        await t.BoardAsync(a, sprint: s1);
        await t.BoardAsync(b, sprint: s1);
        var (_, carried) = await SendAsync(t, HttpMethod.Post, $"/api/sprints/{s1}/carry-over", new { toSprintId = s2 });
        await SendAsync(t, HttpMethod.Post, $"/api/sprints/{s3}/issues", new { issueIds = new[] { b } });

        var undo = await SendAsync(t, HttpMethod.Post, $"/api/sprints/{s1}/carry-over/{Id(carried, "carryOverId")}/undo");

        Assert.Equal(HttpStatusCode.Conflict, undo.Status);
        Assert.Equal(s2, await t.ScalarAsync<Guid>("select sprint_id from board_fields where issue_id = @a", new { a }));
        Assert.Equal(s3, await t.ScalarAsync<Guid>("select sprint_id from board_fields where issue_id = @b", new { b }));
    }

    [DbFact]
    public async Task Idempotent_replay_does_not_repeat_the_change()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var a = await t.AddIssueAsync(await t.AddRepoAsync("r"));
        var s1 = await t.AddSprintAsync("One");
        var s2 = await t.AddSprintAsync("Two");
        var body = new { issueIds = new[] { a } };

        var first = await SendAsync(t, HttpMethod.Post, $"/api/sprints/{s1}/issues", body, key: "k1");
        await t.ExecAsync("update board_fields set sprint_id = @s2 where issue_id = @a", new { s2, a });
        var replay = await SendAsync(t, HttpMethod.Post, $"/api/sprints/{s1}/issues", body, key: "k1");

        Assert.Equal(1, replay.Body.GetProperty("added").GetInt32());
        Assert.Equal(first.Body.GetProperty("sprint").GetProperty("id").GetString(), replay.Body.GetProperty("sprint").GetProperty("id").GetString());
        Assert.Equal(s2, await t.ScalarAsync<Guid>("select sprint_id from board_fields where issue_id = @a", new { a }));
        Assert.Single(await EventsAsync(t, a));
        var reused = await SendAsync(t, HttpMethod.Post, $"/api/sprints/{s2}/issues", body, key: "k1");
        Assert.Equal("idempotency_key_reused", reused.Body.GetProperty("error").GetString());

        var create1 = await SendAsync(t, HttpMethod.Post, "/api/sprints", new { name = "K", startDate = Today(0), endDate = Today(1) }, key: "k2");
        var create2 = await SendAsync(t, HttpMethod.Post, "/api/sprints", new { name = "K", startDate = Today(0), endDate = Today(1) }, key: "k2");
        Assert.Equal(HttpStatusCode.Created, create2.Status);
        Assert.Equal(Id(create1.Body, "id"), Id(create2.Body, "id"));
    }

    [DbFact]
    public async Task Backlog_lists_open_issues_without_a_sprint_with_paging_and_filters()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var project = await t.AddProjectAsync("p", repo);
        var otherRepo = await t.AddRepoAsync("r2");
        var free1 = await t.AddIssueAsync(repo, idle: TimeSpan.FromHours(1));
        var free2 = await t.AddIssueAsync(repo, idle: TimeSpan.FromHours(2));
        var elsewhere = await t.AddIssueAsync(otherRepo);
        var inSprint = await t.AddIssueAsync(repo);
        await t.BoardAsync(inSprint, sprint: await t.AddSprintAsync("S"));
        await t.AddIssueAsync(repo, closed: true);
        var started = await t.AddIssueAsync(repo);
        await t.BoardAsync(started, statusOverride: "in_progress");

        var (_, all) = await SendAsync(t, HttpMethod.Get, "/api/backlog");
        Assert.Equal(4, all.GetProperty("total").GetInt32());
        var ids = all.GetProperty("items").EnumerateArray().Select(x => Id(x, "id")).ToHashSet();
        Assert.Equal(new[] { free1, free2, elsewhere, started }.ToHashSet(), ids);

        var (_, page2) = await SendAsync(t, HttpMethod.Get, "/api/backlog?pageSize=3&page=2");
        Assert.Equal(4, page2.GetProperty("total").GetInt32());
        Assert.Single(page2.GetProperty("items").EnumerateArray());

        var (_, byProject) = await SendAsync(t, HttpMethod.Get, $"/api/backlog?projectId={project}&status=todo");
        Assert.Equal(new[] { free1, free2 }.ToHashSet(), byProject.GetProperty("items").EnumerateArray().Select(x => Id(x, "id")).ToHashSet());
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(t, HttpMethod.Get, "/api/backlog?status=done")).Status);

        await using var other = await TestTenant.CreateAsync(factory);
        Assert.Equal(0, (await SendAsync(other, HttpMethod.Get, "/api/backlog")).Body.GetProperty("total").GetInt32());
    }

    [DbFact]
    public async Task Disabled_sprints_read_empty_and_refuse_writes()
    {
        await using var t = await TestTenant.CreateAsync(factory, sprints: false);
        var issue = await t.AddIssueAsync(await t.AddRepoAsync("r"));
        var sprint = await t.AddSprintAsync("Hidden");

        var (_, list) = await SendAsync(t, HttpMethod.Get, "/api/sprints");
        Assert.Empty(list.GetProperty("current").EnumerateArray());
        Assert.Equal(0, (await SendAsync(t, HttpMethod.Get, "/api/backlog")).Body.GetProperty("total").GetInt32());
        var create = await SendAsync(t, HttpMethod.Post, "/api/sprints", new { name = "n", startDate = Today(0), endDate = Today(1) });
        Assert.Equal(HttpStatusCode.Conflict, create.Status);
        Assert.Equal("sprints_disabled", create.Body.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(t, HttpMethod.Post, $"/api/sprints/{sprint}/issues", new { issueIds = new[] { issue } })).Status);
    }
}
