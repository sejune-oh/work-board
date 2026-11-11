using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WorkBoard.Api.Infrastructure.Identity;
using WorkBoard.Api.Infrastructure.GitHub;
using WorkBoard.Api.Infrastructure.Sync;
using WorkBoard.Api.Tests.Fakes;

namespace WorkBoard.Api.Tests;

public class MirrorWriterTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public MirrorWriterTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private MirrorWriter Writer() => new(_factory.Services.GetRequiredKeyedService<NpgsqlDataSource>(DataKeys.Sync));

    private static SourceUser User(long id, string login = "u") => new(id, login + id, null, null);

    private static SourceIssue Issue(
        long id, int number, DateTimeOffset? updated = null, string title = "t", IReadOnlyList<SourceLabel>? labels = null,
        IReadOnlyList<SourceUser>? assignees = null, long? parent = null, int? milestone = null, string state = "open") =>
        new(id, "node" + id, number, title, "body", null, state, null, User(1), T0, null, updated ?? T0, "https://x/" + number,
            milestone, labels ?? [], assignees ?? [], parent, null);

    private static SourcePullRequest Pr(
        long id, int number, string? body = null, DateTimeOffset? updated = null, params ClosingIssueRef[] closing) =>
        new(id, "pnode" + id, number, "pr", body, "open", false, User(2), T0, updated ?? T0, null, null, "https://x/p" + number,
            true, [], closing);

    private static SourceComment Comment(long id, int issueNumber, DateTimeOffset? updated = null, string body = "c") =>
        new(id, issueNumber, User(3), body, T0, updated ?? T0, null);

    private static RepositorySnapshot Snapshot(
        long repoId, string fullName, IReadOnlyList<SourceIssue>? issues = null, IReadOnlyList<SourcePullRequest>? prs = null,
        IReadOnlyList<SourceComment>? comments = null, IReadOnlyList<SourceLabel>? labels = null,
        IReadOnlyList<SourceMilestone>? milestones = null) =>
        new(new SourceRepository(repoId, fullName[(fullName.IndexOf('/') + 1)..], fullName, "https://x/" + fullName, false),
            milestones ?? [], labels ?? [], issues ?? [], prs ?? [], comments ?? [], null);

    private static async Task<string> DumpAsync(TestTenant t)
    {
        var parts = new List<string>();
        async Task Add(string sql) =>
            parts.Add(string.Join("|", (await Rows(t, sql)).Select(r => string.Join(",", r))));
        await Add("select github_repo_id, name, full_name, html_url, archived from repositories where tenant_id = @tid order by 1");
        await Add("select github_user_id, login from github_users where tenant_id = @tid order by 1");
        await Add("select github_milestone_id, number, title, state, due_on from milestones where tenant_id = @tid order by 1");
        await Add("select github_label_id, name, color from labels where tenant_id = @tid order by 1");
        await Add("""
            select i.github_issue_id, i.number, i.title, i.state, i.milestone_id is not null, p.github_issue_id, i.github_updated_at
              from issues i left join issues p on p.id = i.parent_issue_id where i.tenant_id = @tid order by 1
            """);
        await Add("select i.github_issue_id, l.name from issue_labels il join issues i on i.id = il.issue_id join labels l on l.id = il.label_id where il.tenant_id = @tid order by 1, 2");
        await Add("select i.github_issue_id, a.github_user_id from issue_assignees a join issues i on i.id = a.issue_id where a.tenant_id = @tid order by 1, 2");
        await Add("select github_pr_id, number, state, github_updated_at from pull_requests where tenant_id = @tid order by 1");
        await Add("select i.github_issue_id, p.github_pr_id from issue_pull_requests l join issues i on i.id = l.issue_id join pull_requests p on p.id = l.pull_request_id where l.tenant_id = @tid order by 1, 2");
        await Add("select github_comment_id, body from comments where tenant_id = @tid order by 1");
        return string.Join("\n", parts);
    }

    private static async Task<List<object?[]>> Rows(TestTenant t, string sql)
    {
        var tenantId = t.Id;
        var result = new List<object?[]>();
        var rows = await t.QueryAsync(sql, new { tid = tenantId });
        foreach (IDictionary<string, object?> row in rows)
            result.Add(row.Values.ToArray());
        return result;
    }

    private static SourceMilestone Milestone(long id, int number, string title = "M") =>
        new(id, number, title, "open", T0.AddDays(10), null, "https://x/m" + number);

    private static SourceLabel Label(long id, string name) => new(id, "ln" + id, name, "ff0000", null);

    [DbFact]
    public async Task Applying_the_same_snapshot_twice_gives_identical_rows()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var repoId = TestTenant.NextId();
        var (i1, i2, i3, lab, ms, pr, cm) = (TestTenant.NextId(), TestTenant.NextId(), TestTenant.NextId(), TestTenant.NextId(),
            TestTenant.NextId(), TestTenant.NextId(), TestTenant.NextId());
        var snapshot = Snapshot(repoId, $"org-{t.Suffix}/api",
            issues:
            [
                Issue(i1, 1, labels: [Label(lab, "bug")], assignees: [User(7), User(8)], milestone: 4),
                Issue(i2, 2, parent: i1),
                Issue(i3, 3, parent: null),
            ],
            prs: [Pr(pr, 10, "Closes #2", closing: new ClosingIssueRef(i1, 1, "x/y"))],
            comments: [Comment(cm, 1), Comment(TestTenant.NextId(), 10)],
            labels: [Label(lab, "bug")],
            milestones: [Milestone(ms, 4)]);

        await Writer().ApplyAsync(t.Id, t.ConnectionId, snapshot, default);
        var first = await DumpAsync(t);
        await Writer().ApplyAsync(t.Id, t.ConnectionId, snapshot, default);
        var second = await DumpAsync(t);

        Assert.Equal(first, second);
        Assert.Equal(3, await t.ScalarAsync<long>("select count(*) from issues where tenant_id = @tid", new { tid = t.Id }));
        Assert.Equal(1, await t.ScalarAsync<long>("select count(*) from comments where tenant_id = @tid", new { tid = t.Id }));
        Assert.Equal(2, await t.ScalarAsync<long>("select count(*) from issue_assignees where tenant_id = @tid", new { tid = t.Id }));
    }

    [DbFact]
    public async Task Parent_is_resolved_after_all_issues_of_the_batch_exist_in_either_order()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var (parent, child) = (TestTenant.NextId(), TestTenant.NextId());

        await Writer().ApplyAsync(t.Id, t.ConnectionId,
            Snapshot(TestTenant.NextId(), $"org-{t.Suffix}/api", issues: [Issue(child, 2, parent: parent), Issue(parent, 1)]), default);

        var linked = await t.ScalarAsync<long>("""
            select count(*) from issues c join issues p on p.id = c.parent_issue_id
             where c.tenant_id = @tid and c.github_issue_id = @child and p.github_issue_id = @parent
            """, new { tid = t.Id, child, parent });
        Assert.Equal(1, linked);
    }

    [DbFact]
    public async Task Parent_in_another_repository_is_linked_once_that_repository_syncs()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var (parent, child) = (TestTenant.NextId(), TestTenant.NextId());
        await Writer().ApplyAsync(t.Id, t.ConnectionId,
            Snapshot(TestTenant.NextId(), $"org-{t.Suffix}/a", issues: [Issue(child, 1, parent: parent)]), default);
        Assert.Equal(1, await t.ScalarAsync<long>("select count(*) from issues where tenant_id = @tid and parent_issue_id is null", new { tid = t.Id }));

        await Writer().ApplyAsync(t.Id, t.ConnectionId,
            Snapshot(TestTenant.NextId(), $"org-{t.Suffix}/b", issues: [Issue(parent, 1)]), default);

        Assert.Equal(1, await t.ScalarAsync<long>("select count(*) from issues where tenant_id = @tid and parent_issue_id is not null", new { tid = t.Id }));
    }

    [DbFact]
    public async Task Older_incoming_rows_are_ignored_and_equal_ones_apply()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var (i1, pr, cm) = (TestTenant.NextId(), TestTenant.NextId(), TestTenant.NextId());
        var repo = TestTenant.NextId();
        var name = $"org-{t.Suffix}/api";
        await Writer().ApplyAsync(t.Id, t.ConnectionId, Snapshot(repo, name,
            issues: [Issue(i1, 1, T0.AddHours(2), "new", labels: [Label(1, "a")])], labels: [Label(1, "a")],
            prs: [Pr(pr, 2, updated: T0.AddHours(2))], comments: [Comment(cm, 1, T0.AddHours(2), "new")]), default);

        var result = await Writer().ApplyAsync(t.Id, t.ConnectionId, Snapshot(repo, name,
            issues: [Issue(i1, 1, T0.AddHours(1), "old", labels: [Label(2, "b")])], labels: [Label(1, "a"), Label(2, "b")],
            prs: [Pr(pr, 2, updated: T0.AddHours(1))], comments: [Comment(cm, 1, T0.AddHours(1), "old")]), default);

        Assert.Equal(0, result.Issues + result.PullRequests + result.Comments);
        Assert.Equal("new", await t.ScalarAsync<string>("select title from issues where tenant_id = @tid", new { tid = t.Id }));
        Assert.Equal("new", await t.ScalarAsync<string>("select body from comments where tenant_id = @tid", new { tid = t.Id }));
        Assert.Equal("a", await t.ScalarAsync<string>(
            "select l.name from issue_labels il join labels l on l.id = il.label_id where il.tenant_id = @tid", new { tid = t.Id }));

        var same = await Writer().ApplyAsync(t.Id, t.ConnectionId, Snapshot(repo, name,
            issues: [Issue(i1, 1, T0.AddHours(2), "same-time")], labels: [Label(1, "a")]), default);
        Assert.Equal(1, same.Issues);
        Assert.Equal("same-time", await t.ScalarAsync<string>("select title from issues where tenant_id = @tid", new { tid = t.Id }));
    }

    [DbFact]
    public async Task Another_tenants_rows_are_not_touched()
    {
        await using var a = await TestTenant.CreateAsync(_factory);
        await using var b = await TestTenant.CreateAsync(_factory);
        var (repoId, issueId, labelId) = (TestTenant.NextId(), TestTenant.NextId(), TestTenant.NextId());

        await Writer().ApplyAsync(a.Id, a.ConnectionId, Snapshot(repoId, $"org-{a.Suffix}/api",
            issues: [Issue(issueId, 1, title: "a-title", labels: [Label(labelId, "x")])], labels: [Label(labelId, "x")]), default);
        var before = await DumpAsync(a);

        await Writer().ApplyAsync(b.Id, b.ConnectionId, Snapshot(repoId, $"org-{b.Suffix}/api",
            issues: [Issue(issueId, 1, T0.AddDays(1), "b-title", labels: [Label(labelId, "y")])], labels: [Label(labelId, "y")]), default);

        Assert.Equal(before, await DumpAsync(a));
        Assert.Equal("b-title", await b.ScalarAsync<string>("select title from issues where tenant_id = @tid", new { tid = b.Id }));
    }

    [DbFact]
    public async Task Body_keywords_link_issues_in_the_same_repository_only()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var (i1, i2, i3, i4) = (TestTenant.NextId(), TestTenant.NextId(), TestTenant.NextId(), TestTenant.NextId());
        var (p1, p2) = (TestTenant.NextId(), TestTenant.NextId());
        var repo = TestTenant.NextId();
        var name = $"org-{t.Suffix}/api";

        await Writer().ApplyAsync(t.Id, t.ConnectionId, Snapshot(repo, name,
            issues: [Issue(i1, 1), Issue(i2, 2), Issue(i3, 3), Issue(i4, 4)],
            prs:
            [
                Pr(p1, 10, "closes #1 and Fixes: #2\nresolved #3, see other/repo#4 and also #4"),
                Pr(p2, 11, null, closing: new ClosingIssueRef(i4, 4, name)),
            ]), default);

        var links = await t.QueryAsync("""
            select i.number as issue, p.number as pr from issue_pull_requests l
              join issues i on i.id = l.issue_id join pull_requests p on p.id = l.pull_request_id
             where l.tenant_id = @tid order by 2, 1
            """, new { tid = t.Id });
        Assert.Equal(["10:1", "10:2", "10:3", "11:4"], links.Select(r => $"{(int)r.pr}:{(int)r.issue}").ToArray());

        await Writer().ApplyAsync(t.Id, t.ConnectionId, Snapshot(repo, name,
            prs: [Pr(p1, 10, "Closes #2", updated: T0.AddHours(1))]), default);
        Assert.Equal(1, await t.ScalarAsync<long>(
            "select count(*) from issue_pull_requests l join pull_requests p on p.id = l.pull_request_id where p.number = 10 and l.tenant_id = @tid",
            new { tid = t.Id }));
    }

    [Theory]
    [InlineData("Closes #5", new[] { 5 })]
    [InlineData("CLOSE #5, fixed #6; Resolves: #7", new[] { 5, 6, 7 })]
    [InlineData("closed  #8 fix #9 resolve #10 resolved #11 fixes #12", new[] { 8, 9, 10, 11, 12 })]
    [InlineData("see #5 and org/repo#6 and unfixes #7 and closes#8", new int[0])]
    [InlineData(null, new int[0])]
    public void Closing_keywords_are_parsed(string? body, int[] expected) =>
        Assert.Equal(expected.Order(), ClosingReferences.ParseIssueNumbers(body).Order());

    [DbFact]
    public async Task Labels_can_swap_names_in_one_transaction_and_deleted_labels_go()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var (l1, l2, l3) = (TestTenant.NextId(), TestTenant.NextId(), TestTenant.NextId());
        var repo = TestTenant.NextId();
        var name = $"org-{t.Suffix}/api";
        await Writer().ApplyAsync(t.Id, t.ConnectionId,
            Snapshot(repo, name, labels: [Label(l1, "a"), Label(l2, "b"), Label(l3, "gone")]), default);

        await Writer().ApplyAsync(t.Id, t.ConnectionId,
            Snapshot(repo, name, labels: [Label(l1, "b"), Label(l2, "a")]), default);

        var rows = await t.QueryAsync("select github_label_id as id, name from labels where tenant_id = @tid order by 1", new { tid = t.Id });
        Assert.Equal([$"{l1}:b", $"{l2}:a"], rows.Select(r => $"{(long)r.id}:{(string)r.name}").ToArray());
    }

    [DbFact]
    public async Task Assignees_and_labels_are_replaced_and_repository_full_name_follows_a_rename()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var (i1, l1, l2) = (TestTenant.NextId(), TestTenant.NextId(), TestTenant.NextId());
        var repo = TestTenant.NextId();
        await Writer().ApplyAsync(t.Id, t.ConnectionId, Snapshot(repo, $"org-{t.Suffix}/old",
            issues: [Issue(i1, 1, labels: [Label(l1, "a")], assignees: [User(7)])], labels: [Label(l1, "a"), Label(l2, "b")]), default);

        await Writer().ApplyAsync(t.Id, t.ConnectionId, Snapshot(repo, $"org-{t.Suffix}/new",
            issues: [Issue(i1, 1, T0.AddMinutes(1), labels: [Label(l2, "b")], assignees: [User(8)])],
            labels: [Label(l1, "a"), Label(l2, "b")]), default);

        Assert.Equal("b", await t.ScalarAsync<string>(
            "select l.name from issue_labels il join labels l on l.id = il.label_id where il.tenant_id = @tid", new { tid = t.Id }));
        Assert.Equal(8L, await t.ScalarAsync<long>("select github_user_id from issue_assignees where tenant_id = @tid", new { tid = t.Id }));
        Assert.Equal($"org-{t.Suffix}/new", await t.ScalarAsync<string>("select full_name from repositories where tenant_id = @tid", new { tid = t.Id }));
        Assert.Equal(1, await t.ScalarAsync<long>("select count(*) from repositories where tenant_id = @tid", new { tid = t.Id }));
    }

    [DbFact]
    public async Task Label_without_a_github_id_reuses_the_repository_label_by_name()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var (i1, l1) = (TestTenant.NextId(), TestTenant.NextId());
        var nameless = new SourceLabel(null, null, "bug", "00ff00", null);

        await Writer().ApplyAsync(t.Id, t.ConnectionId, Snapshot(TestTenant.NextId(), $"org-{t.Suffix}/api",
            issues: [Issue(i1, 1, labels: [nameless])], labels: [Label(l1, "bug")]), default);

        Assert.Equal(1, await t.ScalarAsync<long>("select count(*) from labels where tenant_id = @tid", new { tid = t.Id }));
        Assert.Equal(1, await t.ScalarAsync<long>("select count(*) from issue_labels where tenant_id = @tid", new { tid = t.Id }));
    }

    private static SourceRepository SrcRepo(long id, string owner, string name) =>
        new(id, name, $"{owner}/{name}", null, false);

    private static async Task<string[]> RepoNamesAsync(TestTenant t) =>
        (await t.QueryAsync("select name, archived from repositories where tenant_id = @tid order by github_repo_id", new { tid = t.Id }))
        .Select(r => $"{(string)r.name}:{(bool)r.archived}").ToArray();

    [DbFact]
    public async Task A_repository_recreated_with_the_same_name_replaces_the_old_row()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var owner = $"org-{t.Suffix}";
        var (oldId, newId) = (TestTenant.NextId(), TestTenant.NextId());
        await Writer().UpsertRepositoriesAsync(t.Id, t.ConnectionId, [SrcRepo(oldId, owner, "foo")], default);

        await Writer().UpsertRepositoriesAsync(t.Id, t.ConnectionId, [SrcRepo(newId, owner, "foo")], default);

        Assert.Equal([$"foo#{oldId}:True", "foo:False"], await RepoNamesAsync(t));
    }

    [DbFact]
    public async Task A_rename_and_a_new_repository_with_the_old_name_fit_in_one_list_in_either_order()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var owner = $"org-{t.Suffix}";
        var (a, b) = (TestTenant.NextId(), TestTenant.NextId());
        await Writer().UpsertRepositoriesAsync(t.Id, t.ConnectionId, [SrcRepo(a, owner, "foo")], default);

        await Writer().UpsertRepositoriesAsync(t.Id, t.ConnectionId, [SrcRepo(b, owner, "foo"), SrcRepo(a, owner, "bar")], default);

        Assert.Equal(["bar:False", "foo:False"], await RepoNamesAsync(t));
        var (c, d) = (TestTenant.NextId(), TestTenant.NextId());
        await Writer().UpsertRepositoriesAsync(t.Id, t.ConnectionId, [SrcRepo(c, owner, "x")], default);

        await Writer().UpsertRepositoriesAsync(t.Id, t.ConnectionId, [SrcRepo(c, owner, "y"), SrcRepo(d, owner, "x")], default);

        Assert.Equal(1, await t.ScalarAsync<long>(
            "select count(*) from repositories where tenant_id = @tid and name = 'x' and not archived", new { tid = t.Id }));
        Assert.Equal(1, await t.ScalarAsync<long>(
            "select count(*) from repositories where tenant_id = @tid and name = 'y' and not archived", new { tid = t.Id }));
    }

    [DbFact]
    public async Task A_repository_moves_to_another_connection_of_the_tenant()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var other = Guid.NewGuid();
        await t.ExecAsync(
            "insert into github_connections (id, tenant_id, account_id, account_login, account_type) values (@id, @tid, @account, 'p', 'user')",
            new { id = other, tid = t.Id, account = TestTenant.NextId() });
        var id = TestTenant.NextId();
        await Writer().UpsertRepositoriesAsync(t.Id, t.ConnectionId, [SrcRepo(id, "o", "foo")], default);

        await Writer().UpsertRepositoriesAsync(t.Id, other, [SrcRepo(id, "o", "foo")], default);

        Assert.Equal(other, await t.ScalarAsync<Guid>("select connection_id from repositories where tenant_id = @tid", new { tid = t.Id }));
    }

    [DbFact]
    public async Task A_failure_rolls_the_whole_repository_back()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var repo = TestTenant.NextId();
        var bad = Issue(TestTenant.NextId(), 2, state: "weird");

        await Assert.ThrowsAnyAsync<Exception>(() => Writer().ApplyAsync(t.Id, t.ConnectionId,
            Snapshot(repo, $"org-{t.Suffix}/api", issues: [Issue(TestTenant.NextId(), 1), bad]), default));

        Assert.Equal(0, await t.ScalarAsync<long>("select count(*) from issues where tenant_id = @tid", new { tid = t.Id }));
        Assert.Equal(0, await t.ScalarAsync<long>("select count(*) from repositories where tenant_id = @tid", new { tid = t.Id }));
    }
}
