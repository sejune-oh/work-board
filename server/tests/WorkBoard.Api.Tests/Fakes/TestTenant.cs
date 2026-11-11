using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Tests.Fakes;

// A throwaway tenant written through the sync role so tests control every row and timestamp.
// Disposing deletes the tenant; every tenant-owned table cascades from it.
public sealed class TestTenant : IAsyncDisposable
{
    private static long _seq = Random.Shared.NextInt64(10_000_000, 900_000_000);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly NpgsqlDataSource _sync;
    private int _nextNumber = 1;

    public static long NextId() => Interlocked.Increment(ref _seq);

    public Guid Id { get; } = Guid.NewGuid();
    public string Suffix { get; } = Guid.NewGuid().ToString("N")[..8];
    public Guid ConnectionId { get; } = Guid.NewGuid();
    public string ViewerLogin { get; private set; } = "";

    private TestTenant(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _sync = factory.Services.GetRequiredKeyedService<NpgsqlDataSource>(DataKeys.Sync);
    }

    public static async Task<TestTenant> CreateAsync(
        WebApplicationFactory<Program> factory, bool priority = true, bool sprints = true)
    {
        var t = new TestTenant(factory);
        await t.ExecAsync("insert into tenants (id, name) values (@tid, @name)", new { tid = t.Id, name = "org-" + t.Suffix });
        await t.ExecAsync(
            "insert into github_connections (id, tenant_id, account_id, account_login, account_type) values (@id, @tid, @account, @login, 'organization')",
            new { id = t.ConnectionId, tid = t.Id, account = Interlocked.Increment(ref _seq), login = "org-" + t.Suffix });
        await t.ExecAsync(
            "insert into tenant_settings (tenant_id, sprints_enabled, priority_enabled) values (@tid, @sprints, @priority)",
            new { tid = t.Id, sprints, priority });
        (t.ViewerLogin, _) = await t.AddMemberAsync("viewer");
        return t;
    }

    public async Task<int> ExecAsync(string sql, object? param = null)
    {
        await using var conn = await _sync.OpenConnectionAsync();
        return await conn.ExecuteAsync(sql, param);
    }

    public async Task<T> ScalarAsync<T>(string sql, object? param = null)
    {
        await using var conn = await _sync.OpenConnectionAsync();
        return (await conn.QuerySingleAsync<T>(sql, param));
    }

    public async Task<List<dynamic>> QueryAsync(string sql, object? param = null)
    {
        await using var conn = await _sync.OpenConnectionAsync();
        return (await conn.QueryAsync(sql, param)).ToList();
    }

    public async Task<(string Login, long UserId)> AddMemberAsync(string name, long? existingUserId = null)
    {
        var userId = existingUserId ?? Interlocked.Increment(ref _seq);
        var login = $"{name}-{Suffix}";
        await ExecAsync(
            "insert into members (tenant_id, github_user_id, github_login, display_name) values (@tid, @userId, @login, @name)",
            new { tid = Id, userId, login, name });
        return (login, userId);
    }

    public async Task<Guid> AddRepoAsync(string name)
    {
        var id = Guid.NewGuid();
        await ExecAsync(
            "insert into repositories (id, tenant_id, connection_id, github_repo_id, name) values (@id, @tid, @conn, @gid, @name)",
            new { id, tid = Id, conn = ConnectionId, gid = Interlocked.Increment(ref _seq), name });
        return id;
    }

    public async Task<Guid> AddIssueAsync(
        Guid repo, TimeSpan? idle = null, bool closed = false, DateTime? closedAt = null, string? type = null,
        long? assignee = null, string title = "issue")
    {
        var id = Guid.NewGuid();
        var updated = DateTime.UtcNow - (idle ?? TimeSpan.FromHours(1));
        await ExecAsync("""
            insert into issues (id, tenant_id, repository_id, github_issue_id, number, title, issue_type, state,
                                github_created_at, closed_at, github_updated_at)
            values (@id, @tid, @repo, @gid, @number, @title, @type, @state, @created, @closedAt, @updated)
            """, new
        {
            id, tid = Id, repo, gid = Interlocked.Increment(ref _seq), number = _nextNumber++, title, type,
            state = closed ? "closed" : "open", created = updated.AddDays(-30),
            closedAt = closed ? closedAt ?? updated : (DateTime?)null, updated,
        });
        if (assignee is { } userId)
            await AssignAsync(id, userId);
        return id;
    }

    public Task<int> AssignAsync(Guid issue, long userId) =>
        ExecAsync("insert into issue_assignees (tenant_id, issue_id, github_user_id) values (@tid, @issue, @userId)",
            new { tid = Id, issue, userId });

    public Task<int> BoardAsync(Guid issue, string? priority = null, string? statusOverride = null, Guid? sprint = null) =>
        ExecAsync("""
            insert into board_fields (issue_id, tenant_id, priority, status_override, sprint_id)
            values (@issue, @tid, @priority, @statusOverride, @sprint)
            """, new { issue, tid = Id, priority, statusOverride, sprint });

    public async Task<Guid> AddPullRequestAsync(
        Guid repo, Guid? issue, string state = "open", bool review = true, TimeSpan? age = null, long? author = null)
    {
        var id = Guid.NewGuid();
        await ExecAsync("""
            insert into pull_requests (id, tenant_id, repository_id, github_pr_id, number, title, state, review_requested,
                                       author_github_id, github_updated_at)
            values (@id, @tid, @repo, @gid, @number, 'pr', @state, @review, @author, @updated)
            """, new
        {
            id, tid = Id, repo, gid = Interlocked.Increment(ref _seq), number = _nextNumber++, state, review, author,
            updated = DateTime.UtcNow - (age ?? TimeSpan.FromHours(2)),
        });
        if (issue is { } issueId)
            await ExecAsync("insert into issue_pull_requests (tenant_id, issue_id, pull_request_id) values (@tid, @issueId, @id)",
                new { tid = Id, issueId, id });
        return id;
    }

    public async Task<Guid> AddProjectAsync(string name, params Guid[] repos)
    {
        var id = Guid.NewGuid();
        await ExecAsync("insert into projects (id, tenant_id, name) values (@id, @tid, @name)", new { id, tid = Id, name });
        foreach (var repo in repos)
            await ExecAsync("insert into project_repositories (tenant_id, project_id, repository_id) values (@tid, @id, @repo)",
                new { tid = Id, id, repo });
        return id;
    }

    public async Task<Guid> AddSprintAsync(string name, string state = "active")
    {
        var id = Guid.NewGuid();
        await ExecAsync("""
            insert into sprints (id, tenant_id, name, start_date, end_date, state)
            values (@id, @tid, @name, current_date - 2, current_date + 5, @state)
            """, new { id, tid = Id, name, state });
        return id;
    }

    public async Task<Guid> AddNoteAsync(string title, TimeSpan? age = null, Guid? project = null)
    {
        var id = Guid.NewGuid();
        await ExecAsync("insert into meeting_notes (id, tenant_id, project_id, title, held_at) values (@id, @tid, @project, @title, @heldAt)",
            new { id, tid = Id, project, title, heldAt = DateTime.UtcNow - (age ?? TimeSpan.FromHours(5)) });
        return id;
    }

    public async Task<Guid> AddActionItemAsync(Guid note, Guid? issue = null, string text = "todo")
    {
        var id = Guid.NewGuid();
        await ExecAsync("insert into meeting_action_items (id, tenant_id, note_id, text, issue_id) values (@id, @tid, @note, @text, @issue)",
            new { id, tid = Id, note, text, issue });
        return id;
    }

    public async Task<JsonElement> GetAsync(string url, string? login = null)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", login ?? ViewerLogin);
        return await client.GetFromJsonAsync<JsonElement>(url);
    }

    public Task<JsonElement> OverviewAsync() => GetAsync("/api/overview");

    public async ValueTask DisposeAsync()
    {
        await ExecAsync("delete from tenants where id = @tid", new { tid = Id });
    }
}
