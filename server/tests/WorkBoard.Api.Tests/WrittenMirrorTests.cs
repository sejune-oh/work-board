using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WorkBoard.Api.Features.Members;
using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.GitHub;
using WorkBoard.Api.Infrastructure.Identity;
using WorkBoard.Api.Infrastructure.Sync;
using WorkBoard.Api.Infrastructure.Writes;
using WorkBoard.Api.Tests.Fakes;

namespace WorkBoard.Api.Tests;

public class WrittenMirrorTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public WrittenMirrorTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private MirrorWriter Writer() => new(_factory.Services.GetRequiredKeyedService<NpgsqlDataSource>(DataKeys.Sync));

    private static SourceIssue Issue(
        long id, int number, DateTimeOffset updated, string state = "open", string? reason = null, string title = "t",
        IReadOnlyList<SourceLabel>? labels = null, IReadOnlyList<SourceUser>? assignees = null) =>
        new(id, "node" + id, number, title, "body", null, state, reason, new SourceUser(9, "app[bot]", null, null), T0,
            state == "closed" ? updated : null, updated, "https://x/" + number, null, labels ?? [], assignees ?? [], null, null);

    private async Task<Guid> MemberAsync(TestTenant t) =>
        await t.ScalarAsync<Guid>("select id from members where tenant_id = @tid limit 1", new { tid = t.Id });

    [DbFact]
    public async Task Created_issue_is_mirrored_with_labels_and_assignees_and_visible_to_its_tenant_only()
    {
        await using var mine = await TestTenant.CreateAsync(_factory);
        await using var other = await TestTenant.CreateAsync(_factory);
        var repo = await mine.AddRepoAsync("api");
        var id = TestTenant.NextId();
        var labelId = TestTenant.NextId();

        var result = await Writer().ApplyWrittenIssueAsync(mine.Id, repo, Issue(id, 70, T0,
            labels: [new SourceLabel(labelId, "n", "bug", "ff0000", null)],
            assignees: [new SourceUser(21, "alice", null, null)]), null, default);

        Assert.True(result!.Applied);
        Assert.Equal(1, await mine.ScalarAsync<int>("select count(*)::int from issues where tenant_id = @tid and github_issue_id = @id", new { tid = mine.Id, id }));
        Assert.Equal(0, await other.ScalarAsync<int>("select count(*)::int from issues where github_issue_id = @id and tenant_id = @tid", new { tid = other.Id, id }));
        Assert.Equal("bug", await mine.ScalarAsync<string>(
            "select l.name from issue_labels il join labels l on l.id = il.label_id where il.tenant_id = @tid", new { tid = mine.Id }));
        Assert.Equal(21, await mine.ScalarAsync<long>("select github_user_id from issue_assignees where tenant_id = @tid", new { tid = mine.Id }));
        Assert.Equal("app[bot]", await mine.ScalarAsync<string>("select login from github_users where tenant_id = @tid and github_user_id = 9", new { tid = mine.Id }));
    }

    [DbFact]
    public async Task Repository_of_another_tenant_is_refused_and_nothing_is_written()
    {
        await using var mine = await TestTenant.CreateAsync(_factory);
        await using var other = await TestTenant.CreateAsync(_factory);
        var foreignRepo = await other.AddRepoAsync("api");
        var id = TestTenant.NextId();

        var result = await Writer().ApplyWrittenIssueAsync(mine.Id, foreignRepo, Issue(id, 70, T0), null, default);

        Assert.Null(result);
        Assert.Equal(0, await mine.ScalarAsync<int>("select count(*)::int from issues where github_issue_id = @id", new { id }));
    }

    [DbFact]
    public async Task Older_response_does_not_overwrite_newer_data_or_children_or_events()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var repo = await t.AddRepoAsync("api");
        var id = TestTenant.NextId();
        var w = Writer();
        await w.ApplyWrittenIssueAsync(t.Id, repo, Issue(id, 70, T0.AddMinutes(5), title: "new",
            assignees: [new SourceUser(21, "alice", null, null)]), null, default);

        var result = await w.ApplyWrittenIssueAsync(t.Id, repo, Issue(id, 70, T0, state: "closed", reason: "completed", title: "old"),
            await MemberAsync(t), default);

        Assert.False(result!.Applied);
        Assert.Equal("new", await t.ScalarAsync<string>("select title from issues where github_issue_id = @id", new { id }));
        Assert.Equal("open", await t.ScalarAsync<string>("select state from issues where github_issue_id = @id", new { id }));
        Assert.Equal(1, await t.ScalarAsync<int>("select count(*)::int from issue_assignees where tenant_id = @tid", new { tid = t.Id }));
        Assert.Equal(0, await t.ScalarAsync<int>("select count(*)::int from issue_events where tenant_id = @tid", new { tid = t.Id }));
    }

    [DbFact]
    public async Task Close_and_reopen_record_events_with_reason_and_actor()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var repo = await t.AddRepoAsync("api");
        var member = await MemberAsync(t);
        var id = TestTenant.NextId();
        var w = Writer();
        await w.ApplyWrittenIssueAsync(t.Id, repo, Issue(id, 70, T0), member, default);

        await w.ApplyWrittenIssueAsync(t.Id, repo, Issue(id, 70, T0.AddMinutes(1), "closed", "not_planned"), member, default);
        await w.ApplyWrittenIssueAsync(t.Id, repo, Issue(id, 70, T0.AddMinutes(2), "open", "reopened"), member, default);
        // Same state again (e.g. an assignee change) records nothing.
        await w.ApplyWrittenIssueAsync(t.Id, repo, Issue(id, 70, T0.AddMinutes(3), "open", "reopened"), member, default);

        var events = await t.QueryAsync("select kind, from_value, to_value, member_id from issue_events where tenant_id = @tid order by at, kind", new { tid = t.Id });
        Assert.Equal(["closed", "reopened"], events.Select(e => (string)e.kind).OrderBy(k => k).ToArray());
        var closed = events.Single(e => (string)e.kind == "closed");
        Assert.Equal("not_planned", (string)closed.to_value);
        Assert.Equal(member, (Guid)closed.member_id);
        var reopened = events.Single(e => (string)e.kind == "reopened");
        Assert.Equal("not_planned", (string)reopened.from_value);
        Assert.Equal("reopened", (string)reopened.to_value);
    }

    [DbFact]
    public async Task Creating_an_issue_records_no_state_event_and_keeps_the_parent_link()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var repo = await t.AddRepoAsync("api");
        var parent = await t.AddIssueAsync(repo);
        var parentGithubId = await t.ScalarAsync<long>("select github_issue_id from issues where id = @parent", new { parent });
        var id = TestTenant.NextId();
        var w = Writer();
        await w.ApplyWrittenIssueAsync(t.Id, repo, Issue(id, 70, T0), null, default);
        await t.ExecAsync("update issues set parent_github_issue_id = @parentGithubId, parent_issue_id = @parent where github_issue_id = @id",
            new { parentGithubId, parent, id });

        await w.ApplyWrittenIssueAsync(t.Id, repo, Issue(id, 70, T0.AddMinutes(1), title: "renamed"), null, default);

        Assert.Equal(0, await t.ScalarAsync<int>("select count(*)::int from issue_events where tenant_id = @tid", new { tid = t.Id }));
        Assert.Equal(parent, await t.ScalarAsync<Guid>("select parent_issue_id from issues where github_issue_id = @id", new { id }));
        Assert.Equal("renamed", await t.ScalarAsync<string>("select title from issues where github_issue_id = @id", new { id }));
    }

    [DbFact]
    public async Task Comment_is_mirrored_and_an_older_copy_is_ignored()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var repo = await t.AddRepoAsync("api");
        var issue = await t.AddIssueAsync(repo);
        var number = await t.ScalarAsync<int>("select number from issues where id = @issue", new { issue });
        var commentId = TestTenant.NextId();
        var w = Writer();

        Assert.True(await w.ApplyWrittenCommentAsync(t.Id, repo,
            new SourceComment(commentId, number, new SourceUser(9, "app[bot]", null, null), "new", T0, T0.AddMinutes(5), null), default));
        Assert.False(await w.ApplyWrittenCommentAsync(t.Id, repo,
            new SourceComment(commentId, number, new SourceUser(9, "app[bot]", null, null), "old", T0, T0, null), default));
        Assert.False(await w.ApplyWrittenCommentAsync(t.Id, repo,
            new SourceComment(TestTenant.NextId(), 9999, null, "orphan", T0, T0, null), default));

        Assert.Equal("new", await t.ScalarAsync<string>("select body from comments where github_comment_id = @commentId", new { commentId }));
    }

    private (GitHubWriteTargets Targets, ICurrentUser User) TargetsFor(TestTenant t, Guid memberId)
    {
        var user = new CurrentUser();
        user.Set(new DevIdentity(new Member(memberId, 1, t.ViewerLogin, null, null, "member"), t.Id, "t"));
        var db = new TenantDb(user, _factory.Services.GetRequiredService<IAppConnectionFactory>());
        return (new GitHubWriteTargets(db, user), user);
    }

    [DbFact]
    public async Task Write_target_needs_an_active_installed_connection_of_the_callers_tenant()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        await using var other = await TestTenant.CreateAsync(_factory);
        var repo = await t.AddRepoAsync("api");
        var foreign = await other.AddRepoAsync("api");
        var (targets, _) = TargetsFor(t, await MemberAsync(t));

        Assert.Null(await targets.ResolveAsync(repo, default));

        await t.ExecAsync("update github_connections set installation_id = @i where id = @c", new { i = TestTenant.NextId(), c = t.ConnectionId });
        var target = await targets.ResolveAsync(repo, default);
        Assert.Equal(repo, target!.RepositoryId);
        Assert.Equal($"org-{t.Suffix}", target.Repo.Owner);
        Assert.Equal("api", target.Repo.Name);

        Assert.Null(await targets.ResolveAsync(foreign, default));

        await t.ExecAsync("update github_connections set disconnected_at = now() where id = @c", new { c = t.ConnectionId });
        Assert.Null(await targets.ResolveAsync(repo, default));
    }
}
