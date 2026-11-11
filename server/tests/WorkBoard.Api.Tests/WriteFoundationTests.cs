using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WorkBoard.Api.Features.Members;
using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.Identity;
using WorkBoard.Api.Infrastructure.Writes;
using WorkBoard.Api.Tests.Fakes;

namespace WorkBoard.Api.Tests;

public class WriteFoundationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WriteFoundationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private sealed record Actor(TestTenant Tenant, CurrentUser User, TenantDb Db, Guid MemberId, Guid Issue, Guid Sprint);

    private async Task<Actor> ActorAsync(TestTenant? tenant = null)
    {
        tenant ??= await TestTenant.CreateAsync(_factory);
        var memberId = await tenant.ScalarAsync<Guid>("select id from members where tenant_id = @tid limit 1", new { tid = tenant.Id });
        var repo = await tenant.AddRepoAsync("r-" + tenant.Suffix);
        var issue = await tenant.AddIssueAsync(repo);
        var sprint = await tenant.AddSprintAsync("s");

        var user = new CurrentUser();
        user.Set(new DevIdentity(new Member(memberId, 1, tenant.ViewerLogin, null, null, "member"), tenant.Id, "t"));
        var db = new TenantDb(user, _factory.Services.GetRequiredService<IAppConnectionFactory>());
        return new Actor(tenant, user, db, memberId, issue, sprint);
    }

    private async Task RecordAsync(Actor a, TenantSession s, string tag)
    {
        await new AuditLog(a.User).WriteAsync(s, "test." + tag, "issue", a.Issue, new { a = 1 }, new { a = 2 });
        await new IssueEvents(a.User).RecordAsync(s,
            new IssueEvent(a.Issue, IssueEventKind.SprintAdded, null, "s", a.Sprint));
    }

    [DbFact]
    public async Task Audit_and_event_rows_are_written_with_actor_and_json()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);

        await a.Db.WriteAsync(async s => { await RecordAsync(a, s, "x"); return 0; }, default);

        var audit = (await t.QueryAsync("select member_id, action, before::text as b, after::text as af from audit_log where tenant_id = @tid", new { tid = t.Id })).Single();
        Assert.Equal(a.MemberId, (Guid)audit.member_id);
        Assert.Equal("test.x", (string)audit.action);
        Assert.Contains("\"a\"", (string)audit.b);
        var ev = (await t.QueryAsync("select kind, sprint_id, member_id from issue_events where tenant_id = @tid", new { tid = t.Id })).Single();
        Assert.Equal("sprint_added", (string)ev.kind);
        Assert.Equal(a.Sprint, (Guid)ev.sprint_id);
        Assert.Equal(a.MemberId, (Guid)ev.member_id);
    }

    [DbFact]
    public async Task Audit_and_events_roll_back_with_the_transaction()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);

        await Assert.ThrowsAsync<InvalidOperationException>(() => a.Db.WriteAsync<int>(async s =>
        {
            await RecordAsync(a, s, "x");
            throw new InvalidOperationException("boom");
        }, default));

        Assert.Equal(0, await t.ScalarAsync<int>("select count(*)::int from audit_log where tenant_id = @tid", new { tid = t.Id }));
        Assert.Equal(0, await t.ScalarAsync<int>("select count(*)::int from issue_events where tenant_id = @tid", new { tid = t.Id }));
    }

    [DbFact]
    public async Task Unknown_event_kind_is_rejected_by_the_database()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => a.Db.WriteAsync(async s =>
        {
            await new IssueEvents(a.User).RecordAsync(s, new IssueEvent(a.Issue, "bogus"));
            return 0;
        }, default));

        Assert.Equal("23514", ex.SqlState);
    }

    [DbFact]
    public async Task Tables_are_isolated_between_tenants()
    {
        await using var t1 = await TestTenant.CreateAsync(_factory);
        await using var t2 = await TestTenant.CreateAsync(_factory);
        var a1 = await ActorAsync(t1);
        var a2 = await ActorAsync(t2);

        await a1.Db.WriteAsync(async s =>
        {
            await RecordAsync(a1, s, "one");
            await Idempotency.RunAsync(s, a1.User, "k", "h", () => Task.FromResult(new WriteResult(201, new { ok = 1 })));
            return 0;
        }, default);

        foreach (var table in new[] { "audit_log", "issue_events", "idempotency_keys" })
        {
            var seenByOther = await a2.Db.ReadAsync(s => s.QuerySingleOrDefaultAsync<int>($"select count(*)::int from {table}"), default);
            var seenByOwner = await a1.Db.ReadAsync(s => s.QuerySingleOrDefaultAsync<int>($"select count(*)::int from {table}"), default);
            Assert.Equal(0, seenByOther);
            Assert.Equal(1, seenByOwner);
        }

        // Writing a row for another tenant is refused by the RLS check.
        var ex = await Assert.ThrowsAsync<PostgresException>(() => a2.Db.WriteAsync(s => s.ExecuteAsync(
            "insert into audit_log (tenant_id, action, target_type) values (@other, 'x', 'issue')", new { other = t1.Id }), default));
        Assert.Equal("42501", ex.SqlState);
    }

    [DbTheory]
    [InlineData("update audit_log set action = 'x'")]
    [InlineData("delete from audit_log")]
    [InlineData("update issue_events set kind = 'size_set'")]
    [InlineData("delete from issue_events")]
    [InlineData("update idempotency_keys set status_code = 500")]
    public async Task App_role_cannot_rewrite_history(string sql)
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => a.Db.WriteAsync(s => s.ExecuteAsync(sql), default));

        Assert.Equal("42501", ex.SqlState);
    }

    private Task<IdempotentResponse> RunKeyAsync(Actor a, string? key, string hash, Func<Task<WriteResult>> run) =>
        a.Db.WriteAsync(s => Idempotency.RunAsync(s, a.User, key, hash, run), default);

    [DbFact]
    public async Task Same_key_and_request_replays_the_stored_response_without_running_again()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);
        var runs = 0;
        Task<WriteResult> Run() { runs++; return Task.FromResult(new WriteResult(201, new { id = "abc" })); }

        var first = await RunKeyAsync(a, "key-1", "h1", Run);
        var second = await RunKeyAsync(a, "key-1", "h1", Run);

        Assert.Equal(1, runs);
        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Equal(201, second.StatusCode);
        Assert.Contains("\"abc\"", second.Json);
    }

    [DbFact]
    public async Task Same_key_with_a_different_request_is_409()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);
        var runs = 0;
        Task<WriteResult> Run() { runs++; return Task.FromResult(new WriteResult(200)); }

        await RunKeyAsync(a, "key-1", "h1", Run);
        var second = await RunKeyAsync(a, "key-1", "h2", Run);

        Assert.Equal(1, runs);
        Assert.Equal(409, second.StatusCode);
        Assert.Contains("idempotency_key_reused", second.Json);
    }

    [DbFact]
    public async Task Missing_key_always_runs_and_stores_nothing()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);
        var runs = 0;
        Task<WriteResult> Run() { runs++; return Task.FromResult(new WriteResult(200)); }

        await RunKeyAsync(a, null, "h", Run);
        await RunKeyAsync(a, null, "h", Run);

        Assert.Equal(2, runs);
        Assert.Equal(0, await t.ScalarAsync<int>("select count(*)::int from idempotency_keys where tenant_id = @tid", new { tid = t.Id }));
    }

    [DbFact]
    public async Task Failed_results_are_not_stored_and_oversized_keys_are_rejected()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);

        var failed = await RunKeyAsync(a, "k", "h", () => Task.FromResult(new WriteResult(404)));
        var retried = await RunKeyAsync(a, "k", "h", () => Task.FromResult(new WriteResult(200)));
        var tooLong = await RunKeyAsync(a, new string('x', Idempotency.KeyMaxLength + 1), "h", () => Task.FromResult(new WriteResult(200)));

        Assert.Equal(404, failed.StatusCode);
        Assert.Equal(200, retried.StatusCode);
        Assert.False(retried.Replayed);
        Assert.Equal(400, tooLong.StatusCode);
    }

    [DbFact]
    public async Task Keys_older_than_24_hours_are_ignored_and_replaced()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);
        await RunKeyAsync(a, "old", "h1", () => Task.FromResult(new WriteResult(201, new { v = 1 })));
        await t.ExecAsync("update idempotency_keys set created_at = now() - interval '25 hours' where tenant_id = @tid", new { tid = t.Id });

        var again = await RunKeyAsync(a, "old", "h2", () => Task.FromResult(new WriteResult(201, new { v = 2 })));

        Assert.False(again.Replayed);
        Assert.Contains("2", again.Json);
        Assert.Equal("h2", await t.ScalarAsync<string>("select request_hash from idempotency_keys where tenant_id = @tid", new { tid = t.Id }));
    }

    [DbFact]
    public async Task Stored_key_rolls_back_with_the_change()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);

        await Assert.ThrowsAsync<InvalidOperationException>(() => a.Db.WriteAsync<int>(async s =>
        {
            await Idempotency.RunAsync(s, a.User, "k", "h", () => Task.FromResult(new WriteResult(200)));
            throw new InvalidOperationException("boom");
        }, default));

        Assert.Equal(0, await t.ScalarAsync<int>("select count(*)::int from idempotency_keys where tenant_id = @tid", new { tid = t.Id }));
    }

    [Fact]
    public void Request_hash_depends_on_member_method_path_and_body()
    {
        var m = Guid.NewGuid();
        var baseHash = Idempotency.HashRequest(m, "POST", "/api/x", "{}"u8);

        Assert.Equal(baseHash, Idempotency.HashRequest(m, "post", "/api/x", "{}"u8));
        Assert.NotEqual(baseHash, Idempotency.HashRequest(m, "POST", "/api/x", "{ }"u8));
        Assert.NotEqual(baseHash, Idempotency.HashRequest(m, "POST", "/api/y", "{}"u8));
        Assert.NotEqual(baseHash, Idempotency.HashRequest(Guid.NewGuid(), "POST", "/api/x", "{}"u8));
    }
}
