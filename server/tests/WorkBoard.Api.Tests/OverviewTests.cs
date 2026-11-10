using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WorkBoard.Api.Tests.Fakes;

namespace WorkBoard.Api.Tests;

public class OverviewTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly TimeSpan Fresh = TimeSpan.FromHours(1);

    private static DateTime WeekStartUtc()
    {
        var kst = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, kst);
        var monday = local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7));
        return TimeZoneInfo.ConvertTimeToUtc(monday, kst);
    }

    private static List<string> Ids(JsonElement array, params string[] path) =>
        array.EnumerateArray().Select(e => path.Aggregate(e, (x, p) => x.GetProperty(p)).GetString()!).ToList();

    [DbFact]
    public async Task Counts_split_open_by_status_and_stale_threshold()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        await t.AddIssueAsync(repo);                                                  // todo
        var inProgress = await t.AddIssueAsync(repo);
        await t.BoardAsync(inProgress, statusOverride: "in_progress");
        var inReview = await t.AddIssueAsync(repo);
        await t.AddPullRequestAsync(repo, inReview, review: true);
        await t.AddIssueAsync(repo, idle: TimeSpan.FromDays(6) + TimeSpan.FromHours(23)); // not stale yet
        await t.AddIssueAsync(repo, idle: TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1)); // stale
        await t.AddIssueAsync(repo, closed: true, idle: TimeSpan.FromDays(30));         // closed, never stale

        var counts = (await t.OverviewAsync()).GetProperty("counts");

        Assert.Equal(5, counts.GetProperty("open").GetInt32());
        Assert.Equal(1, counts.GetProperty("inProgress").GetInt32());
        Assert.Equal(1, counts.GetProperty("inReview").GetInt32());
        Assert.Equal(1, counts.GetProperty("stale").GetInt32());
    }

    [DbFact]
    public async Task Urgent_lists_open_urgent_issues_and_flags_not_started()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var notStarted = await t.AddIssueAsync(repo, assignee: 0);
        await t.BoardAsync(notStarted, priority: "urgent");
        var started = await t.AddIssueAsync(repo);
        await t.BoardAsync(started, priority: "urgent", statusOverride: "in_progress");
        var done = await t.AddIssueAsync(repo, closed: true);
        await t.BoardAsync(done, priority: "urgent");
        var high = await t.AddIssueAsync(repo);
        await t.BoardAsync(high, priority: "high");

        var urgent = (await t.OverviewAsync()).GetProperty("attention").GetProperty("urgent");

        var byId = urgent.EnumerateArray().ToDictionary(e => e.GetProperty("issue").GetProperty("id").GetString()!);
        Assert.Equal(new[] { notStarted.ToString(), started.ToString() }.Order(), byId.Keys.Order());
        Assert.True(byId[notStarted.ToString()].GetProperty("notStarted").GetBoolean());
        Assert.False(byId[started.ToString()].GetProperty("notStarted").GetBoolean());
    }

    [DbFact]
    public async Task Urgent_not_started_total_counts_beyond_the_first_five()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        for (var i = 0; i < 5; i++)
        {
            var started = await t.AddIssueAsync(repo, idle: TimeSpan.FromDays(10 + i));
            await t.BoardAsync(started, priority: "urgent", statusOverride: "in_progress");
        }
        for (var i = 0; i < 3; i++)
        {
            var notStarted = await t.AddIssueAsync(repo, idle: TimeSpan.FromHours(1 + i));
            await t.BoardAsync(notStarted, priority: "urgent");
        }

        var attention = (await t.OverviewAsync()).GetProperty("attention");

        Assert.All(attention.GetProperty("urgent").EnumerateArray(), u => Assert.False(u.GetProperty("notStarted").GetBoolean()));
        Assert.Equal(8, attention.GetProperty("totals").GetProperty("urgent").GetInt32());
        Assert.Equal(3, attention.GetProperty("totals").GetProperty("urgentNotStarted").GetInt32());
    }

    [DbFact]
    public async Task Urgent_is_empty_when_priority_is_disabled()
    {
        await using var t = await TestTenant.CreateAsync(factory, priority: false);
        var repo = await t.AddRepoAsync("r");
        var issue = await t.AddIssueAsync(repo);
        await t.BoardAsync(issue, priority: "urgent");

        var attention = (await t.OverviewAsync()).GetProperty("attention");

        Assert.Equal(0, attention.GetProperty("urgent").GetArrayLength());
        Assert.Equal(0, attention.GetProperty("totals").GetProperty("urgent").GetInt32());
    }

    [DbFact]
    public async Task Review_waiting_uses_open_requested_prs_linked_to_open_issues()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var (_, author) = await t.AddMemberAsync("author");
        var waiting = await t.AddIssueAsync(repo, assignee: 0);
        await t.AddPullRequestAsync(repo, waiting, review: true, age: TimeSpan.FromHours(30), author: author);
        var noReview = await t.AddIssueAsync(repo);
        await t.AddPullRequestAsync(repo, noReview, review: false);
        var merged = await t.AddIssueAsync(repo);
        await t.AddPullRequestAsync(repo, merged, state: "merged", review: true);
        var closedIssue = await t.AddIssueAsync(repo, closed: true);
        await t.AddPullRequestAsync(repo, closedIssue, review: true);
        await t.AddPullRequestAsync(repo, null, review: true);

        var group = (await t.OverviewAsync()).GetProperty("attention").GetProperty("reviewWaiting");

        var only = Assert.Single(group.EnumerateArray());
        Assert.Equal(waiting.ToString(), only.GetProperty("issue").GetProperty("id").GetString());
        var pr = only.GetProperty("pullRequest");
        Assert.StartsWith("author-", pr.GetProperty("authorGithubLogin").GetString());
        Assert.InRange(DateTime.UtcNow - pr.GetProperty("waitingSince").GetDateTime(),
            TimeSpan.FromHours(29), TimeSpan.FromHours(31));
    }

    [DbFact]
    public async Task Issue_appears_only_in_the_first_matching_group_and_totals_precede_the_cut()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var urgentAndStale = await t.AddIssueAsync(repo, idle: TimeSpan.FromDays(20));
        await t.BoardAsync(urgentAndStale, priority: "urgent");
        for (var i = 0; i < 6; i++)
            await t.AddIssueAsync(repo, idle: TimeSpan.FromDays(10 + i), assignee: 0);

        var attention = (await t.OverviewAsync()).GetProperty("attention");

        Assert.Equal(1, attention.GetProperty("totals").GetProperty("urgent").GetInt32());
        var stale = attention.GetProperty("stale");
        Assert.DoesNotContain(urgentAndStale.ToString(), Ids(stale, "issue", "id"));
        Assert.Equal(5, stale.GetArrayLength());
        Assert.Equal(6, attention.GetProperty("totals").GetProperty("stale").GetInt32());
        Assert.True(stale[0].GetProperty("idleDays").GetInt32() >= stale[4].GetProperty("idleDays").GetInt32());
    }

    [DbFact]
    public async Task Unassigned_skips_assigned_closed_and_epics()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var (_, user) = await t.AddMemberAsync("dev");
        var loose = await t.AddIssueAsync(repo);
        await t.AddIssueAsync(repo, assignee: user);
        await t.AddIssueAsync(repo, type: "Epic");
        await t.AddIssueAsync(repo, closed: true);

        var unassigned = (await t.OverviewAsync()).GetProperty("attention").GetProperty("unassigned");

        Assert.Equal([loose.ToString()], Ids(unassigned, "id"));
    }

    [DbFact]
    public async Task Action_items_without_issue_reach_the_attention_list()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var issue = await t.AddIssueAsync(repo, assignee: 0);
        var note = await t.AddNoteAsync("weekly");
        var orphan = await t.AddActionItemAsync(note, text: "no issue yet");
        await t.AddActionItemAsync(note, issue, "linked");

        var overview = await t.OverviewAsync();

        var group = overview.GetProperty("attention").GetProperty("actionItemsWithoutIssue");
        Assert.Equal([orphan.ToString()], Ids(group, "id"));
        Assert.Equal("weekly", group[0].GetProperty("note").GetProperty("title").GetString());
        Assert.Equal(1, overview.GetProperty("attention").GetProperty("totals").GetProperty("actionItemsWithoutIssue").GetInt32());
    }

    [DbFact]
    public async Task Action_items_list_is_limited_to_14_days_and_drops_finished_work()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var open = await t.AddIssueAsync(repo);
        var closed = await t.AddIssueAsync(repo, closed: true);
        var recent = await t.AddNoteAsync("recent", TimeSpan.FromDays(3));
        var old = await t.AddNoteAsync("old", TimeSpan.FromDays(15));
        var keepNoIssue = await t.AddActionItemAsync(recent);
        var keepOpen = await t.AddActionItemAsync(recent, open);
        await t.AddActionItemAsync(recent, closed);
        await t.AddActionItemAsync(old);

        var items = (await t.OverviewAsync()).GetProperty("actionItems");

        Assert.Equal(new[] { keepNoIssue.ToString(), keepOpen.ToString() }.Order(), Ids(items, "id").Order());
    }

    [DbFact]
    public async Task Done_this_week_starts_monday_midnight_in_seoul()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var (login, user) = await t.AddMemberAsync("closer");
        var monday = WeekStartUtc();
        await t.AddIssueAsync(repo, closed: true, closedAt: monday.AddSeconds(-1), assignee: user);
        await t.AddIssueAsync(repo, closed: true, closedAt: monday.AddSeconds(1), assignee: user);
        await t.AddIssueAsync(repo, closed: true, closedAt: monday, assignee: user);

        var overview = await t.OverviewAsync();

        Assert.Equal(2, overview.GetProperty("counts").GetProperty("doneThisWeek").GetInt32());
        var person = overview.GetProperty("people").EnumerateArray()
            .Single(p => p.GetProperty("member").GetProperty("githubLogin").GetString() == login);
        Assert.Equal(2, person.GetProperty("doneThisWeek").GetInt32());
    }

    [DbFact]
    public async Task Overloaded_needs_four_in_progress_and_twice_the_team_median()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var (busy, busyId) = await t.AddMemberAsync("busy");
        var (light, lightId) = await t.AddMemberAsync("light");
        for (var i = 0; i < 4; i++)
        {
            var issue = await t.AddIssueAsync(repo, assignee: busyId);
            await t.BoardAsync(issue, statusOverride: i % 2 == 0 ? "in_progress" : "in_review");
        }
        for (var i = 0; i < 3; i++)
        {
            var issue = await t.AddIssueAsync(repo, assignee: lightId);
            await t.BoardAsync(issue, statusOverride: "in_progress");
        }

        var people = (await t.OverviewAsync()).GetProperty("people").EnumerateArray()
            .ToDictionary(p => p.GetProperty("member").GetProperty("githubLogin").GetString()!);

        // counts are [0 (viewer), 4, 3]: median 3, so 4 < 6 is not overloaded
        Assert.False(people[busy].GetProperty("overloaded").GetBoolean());

        for (var i = 0; i < 2; i++)
        {
            var issue = await t.AddIssueAsync(repo, assignee: busyId);
            await t.BoardAsync(issue, statusOverride: "in_progress");
        }
        people = (await t.OverviewAsync()).GetProperty("people").EnumerateArray()
            .ToDictionary(p => p.GetProperty("member").GetProperty("githubLogin").GetString()!);

        // counts are [0, 6, 3]: 6 >= 2 * 3
        Assert.True(people[busy].GetProperty("overloaded").GetBoolean());
        Assert.False(people[light].GetProperty("overloaded").GetBoolean());
        Assert.Equal(busy, people.Values.First().GetProperty("member").GetProperty("githubLogin").GetString());
    }

    [DbFact]
    public async Task Person_current_is_the_most_recent_in_progress_or_review_issue()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var (login, user) = await t.AddMemberAsync("dev");
        var older = await t.AddIssueAsync(repo, idle: TimeSpan.FromHours(10), assignee: user);
        await t.BoardAsync(older, statusOverride: "in_progress");
        var newer = await t.AddIssueAsync(repo, idle: TimeSpan.FromHours(2), assignee: user);
        await t.BoardAsync(newer, statusOverride: "in_review");
        await t.AddIssueAsync(repo, idle: TimeSpan.FromMinutes(1), assignee: user);   // todo, ignored

        var person = (await t.OverviewAsync()).GetProperty("people").EnumerateArray()
            .Single(p => p.GetProperty("member").GetProperty("githubLogin").GetString() == login);

        Assert.Equal(newer.ToString(), person.GetProperty("current").GetProperty("id").GetString());
        Assert.NotEqual(JsonValueKind.Null, person.GetProperty("lastActivityAt").ValueKind);
    }

    [DbFact]
    public async Task Project_progress_counts_every_issue_not_only_recent_closures()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var project = await t.AddProjectAsync("p", repo);
        await t.AddIssueAsync(repo, closed: true, closedAt: DateTime.UtcNow.AddDays(-200), idle: TimeSpan.FromDays(200));
        await t.AddIssueAsync(repo, closed: true, closedAt: DateTime.UtcNow.AddDays(-1));
        await t.AddIssueAsync(repo);
        var review = await t.AddIssueAsync(repo);
        await t.AddPullRequestAsync(repo, review, review: true);
        await t.AddPullRequestAsync(repo, null, state: "merged");

        var entry = (await t.OverviewAsync()).GetProperty("projects").EnumerateArray()
            .Single(p => p.GetProperty("project").GetProperty("id").GetString() == project.ToString());

        var by = entry.GetProperty("byStatus");
        Assert.Equal(2, by.GetProperty("done").GetInt32());
        Assert.Equal(1, by.GetProperty("todo").GetInt32());
        Assert.Equal(1, by.GetProperty("inReview").GetInt32());
        Assert.Equal(1, entry.GetProperty("openPullRequests").GetInt32());
        Assert.Equal(["r"], entry.GetProperty("project").GetProperty("repositories").EnumerateArray().Select(r => r.GetString()));
    }

    [DbFact]
    public async Task Sprint_is_the_active_one_with_status_counts_and_null_when_disabled()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var active = await t.AddSprintAsync("active one");
        await t.AddSprintAsync("old", "closed");
        var a = await t.AddIssueAsync(repo);
        await t.BoardAsync(a, sprint: active);
        var b = await t.AddIssueAsync(repo, closed: true);
        await t.BoardAsync(b, sprint: active);

        var sprint = (await t.OverviewAsync()).GetProperty("sprint");

        Assert.Equal("active one", sprint.GetProperty("name").GetString());
        Assert.Equal(1, sprint.GetProperty("byStatus").GetProperty("todo").GetInt32());
        Assert.Equal(1, sprint.GetProperty("byStatus").GetProperty("done").GetInt32());
        Assert.InRange(sprint.GetProperty("daysLeft").GetInt32(), 4, 5);

        await using var off = await TestTenant.CreateAsync(factory, sprints: false);
        await off.AddSprintAsync("hidden");
        Assert.Equal(JsonValueKind.Null, (await off.OverviewAsync()).GetProperty("sprint").ValueKind);
    }

    [DbFact]
    public async Task Milestones_are_null_until_sync_provides_them()
    {
        await using var t = await TestTenant.CreateAsync(factory);

        var overview = await t.OverviewAsync();

        Assert.Equal(JsonValueKind.Null, overview.GetProperty("milestones").ValueKind);
        Assert.Equal(0, overview.GetProperty("counts").GetProperty("open").GetInt32());
        Assert.Equal(0, overview.GetProperty("activity").GetArrayLength());
    }

    [DbFact]
    public async Task Activity_is_newest_first_within_three_days_and_capped_at_twelve()
    {
        await using var t = await TestTenant.CreateAsync(factory);
        var repo = await t.AddRepoAsync("r");
        var issue = await t.AddIssueAsync(repo);
        for (var i = 0; i < 14; i++)
            await t.ExecAsync("""
                insert into comments (tenant_id, issue_id, github_comment_id, body, github_created_at, github_updated_at)
                values (@tid, @issue, @cid, @body, now() - make_interval(mins => @m), now())
                """, new { tid = t.Id, issue, cid = 7_000_000_000L + i + Random.Shared.NextInt64(1_000_000), body = new string('x', 100), m = i + 1 });
        await t.AddNoteAsync("old note", TimeSpan.FromDays(4));
        await t.AddPullRequestAsync(repo, null, state: "closed");

        var activity = (await t.OverviewAsync()).GetProperty("activity");

        Assert.Equal(12, activity.GetArrayLength());
        Assert.All(activity.EnumerateArray(), a => Assert.Equal("comment", a.GetProperty("kind").GetString()));
        Assert.Equal(80, activity[0].GetProperty("snippet").GetString()!.Length);
        var times = activity.EnumerateArray().Select(a => a.GetProperty("at").GetDateTime()).ToList();
        Assert.Equal(times.OrderByDescending(x => x), times);
    }

    [DbFact]
    public async Task Overview_only_sees_the_callers_tenant()
    {
        await using var a = await TestTenant.CreateAsync(factory);
        await using var b = await TestTenant.CreateAsync(factory);
        var repoA = await a.AddRepoAsync("ra");
        var repoB = await b.AddRepoAsync("rb");
        await a.AddIssueAsync(repoA);
        for (var i = 0; i < 3; i++)
            await b.AddIssueAsync(repoB, idle: TimeSpan.FromDays(30));
        await b.AddNoteAsync("secret");

        var overview = await a.OverviewAsync();

        Assert.Equal(1, overview.GetProperty("counts").GetProperty("open").GetInt32());
        Assert.Equal(0, overview.GetProperty("counts").GetProperty("stale").GetInt32());
        Assert.Equal(1, overview.GetProperty("people").GetArrayLength());
        Assert.DoesNotContain("secret", overview.GetRawText());
        Assert.DoesNotContain(b.Suffix, overview.GetRawText());
    }
}
