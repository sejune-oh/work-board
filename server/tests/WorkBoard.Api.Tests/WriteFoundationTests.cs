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

    private Task<IdempotentResponse> ExternalAsync(
        Actor a, string key, string hash, Func<Task<ExternalOutcome>> call) =>
        Idempotency.RunExternalAsync(a.Db, a.User, key, hash, _ => call(), default);

    private static Task<ExternalOutcome> Ok(object body) =>
        Task.FromResult(new ExternalOutcome(new WriteResult(201, body), true));

    [DbFact]
    public async Task External_write_replays_after_completion()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);
        var calls = 0;

        var first = await ExternalAsync(a, "k", "h", () => { calls++; return Ok(new { n = 1 }); });
        var second = await ExternalAsync(a, "k", "h", () => { calls++; return Ok(new { n = 2 }); });

        Assert.Equal(1, calls);
        Assert.True(second.Replayed);
        Assert.Equal(first.Json, second.Json!.Replace(" ", ""));
        Assert.Equal("completed", await t.ScalarAsync<string>("select state from idempotency_keys where tenant_id = @tid", new { tid = t.Id }));
    }

    [DbFact]
    public async Task External_write_in_flight_is_409_request_in_progress_and_holds_no_transaction()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);
        IdempotentResponse? during = null;
        string? stateDuring = null;

        await ExternalAsync(a, "k", "h", async () =>
        {
            stateDuring = await t.ScalarAsync<string>("select state from idempotency_keys where tenant_id = @tid", new { tid = t.Id });
            during = await ExternalAsync(a, "k", "h", () => throw new InvalidOperationException("must not run"));
            return (await Ok(new { }));
        });

        Assert.Equal("pending", stateDuring);
        Assert.Equal(409, during!.StatusCode);
        Assert.Contains("request_in_progress", during.Json);
    }

    [DbFact]
    public async Task Stale_pending_reservation_is_taken_over()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);
        await t.ExecAsync("""
            insert into idempotency_keys (tenant_id, key, member_id, request_hash, state, created_at)
            values (@tid, 'k', @m, 'old', 'pending', now() - interval '3 minutes')
            """, new { tid = t.Id, m = a.MemberId });
        var calls = 0;

        var result = await ExternalAsync(a, "k", "h", () => { calls++; return Ok(new { }); });

        Assert.Equal(1, calls);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal("h", await t.ScalarAsync<string>("select request_hash from idempotency_keys where tenant_id = @tid", new { tid = t.Id }));
    }

    [DbFact]
    public async Task External_write_with_a_different_request_is_409_reused()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);
        await ExternalAsync(a, "k", "h1", () => Ok(new { }));

        var second = await ExternalAsync(a, "k", "h2", () => throw new InvalidOperationException("must not run"));

        Assert.Equal(409, second.StatusCode);
        Assert.Contains("idempotency_key_reused", second.Json);
    }

    [DbFact]
    public async Task Failure_before_the_external_call_takes_effect_allows_a_retry()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ExternalAsync(a, "k", "h", () => throw new InvalidOperationException("boom")));
        var noEffect = await ExternalAsync(a, "k", "h",
            () => Task.FromResult(new ExternalOutcome(new WriteResult(502), false)));
        var retried = await ExternalAsync(a, "k", "h", () => Ok(new { }));

        Assert.Equal(502, noEffect.StatusCode);
        Assert.False(retried.Replayed);
        Assert.Equal(201, retried.StatusCode);
    }

    [DbFact]
    public async Task Non_2xx_after_a_possible_side_effect_is_stored_and_replayed()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);
        var calls = 0;
        Task<ExternalOutcome> Call() { calls++; return Task.FromResult(new ExternalOutcome(new WriteResult(502, new { error = "github_unavailable" }), true)); }

        await ExternalAsync(a, "k", "h", Call);
        var again = await ExternalAsync(a, "k", "h", Call);

        Assert.Equal(1, calls);
        Assert.True(again.Replayed);
        Assert.Equal(502, again.StatusCode);
    }

    [DbFact]
    public async Task Stale_owners_late_complete_does_not_touch_the_new_owners_row()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);

        await ExternalAsync(a, "k", "hA", async () =>
        {
            await t.ExecAsync("update idempotency_keys set created_at = now() - interval '3 minutes' where tenant_id = @tid", new { tid = t.Id });
            await ExternalAsync(a, "k", "hB", () => Ok(new { owner = "B" }));
            return await Ok(new { owner = "A" });
        });

        var row = (await t.QueryAsync("select request_hash, response::text as r from idempotency_keys where tenant_id = @tid", new { tid = t.Id })).Single();
        Assert.Equal("hB", (string)row.request_hash);
        Assert.Contains("B", (string)row.r);
    }

    [DbFact]
    public async Task Stale_owners_release_does_not_delete_the_new_owners_reservation()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);
        var gate = new TaskCompletionSource();
        Task<IdempotentResponse>? b = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => ExternalAsync(a, "k", "hA", async () =>
        {
            await t.ExecAsync("update idempotency_keys set created_at = now() - interval '3 minutes' where tenant_id = @tid", new { tid = t.Id });
            b = ExternalAsync(a, "k", "hB", async () => { await gate.Task; return await Ok(new { owner = "B" }); });
            while (await t.ScalarAsync<int>("select count(*)::int from idempotency_keys where tenant_id = @tid and request_hash = 'hB'", new { tid = t.Id }) == 0)
                await Task.Delay(20);
            throw new InvalidOperationException("A failed");
        }));

        Assert.Equal("pending", await t.ScalarAsync<string>("select state from idempotency_keys where tenant_id = @tid", new { tid = t.Id }));
        gate.SetResult();
        Assert.Equal(201, (await b!).StatusCode);
        Assert.Equal("completed", await t.ScalarAsync<string>("select state from idempotency_keys where tenant_id = @tid", new { tid = t.Id }));
    }

    [DbFact]
    public async Task Pending_key_blocks_the_db_only_mode_too()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);
        await t.ExecAsync("""
            insert into idempotency_keys (tenant_id, key, member_id, request_hash, state)
            values (@tid, 'k', @m, 'h', 'pending')
            """, new { tid = t.Id, m = a.MemberId });

        var result = await RunKeyAsync(a, "k", "h", () => Task.FromResult(new WriteResult(200)));

        Assert.Contains("request_in_progress", result.Json);
    }

    [DbFact]
    public async Task Closed_and_reopened_events_are_accepted()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var a = await ActorAsync(t);

        await a.Db.WriteAsync(async s =>
        {
            var events = new IssueEvents(a.User);
            await events.RecordAsync(s, new IssueEvent(a.Issue, IssueEventKind.Closed, "open", "completed"));
            await events.RecordAsync(s, new IssueEvent(a.Issue, IssueEventKind.Reopened, "completed", "reopened"));
            return 0;
        }, default);

        Assert.Equal(2, await t.ScalarAsync<int>("select count(*)::int from issue_events where tenant_id = @tid", new { tid = t.Id }));
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
