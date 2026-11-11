using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Tests;

public class SyncSchemaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Acme = "10000000-0000-0000-0000-000000000001";
    private const string Globex = "10000000-0000-0000-0000-000000000002";
    private const string AcmeRepo = "30000000-0000-0000-0000-000000000001";
    private const string AcmeIssue = "50000000-0000-0000-0000-000000000002";
    private const string AcmeMilestone = "b1000000-0000-0000-0000-000000000001";
    private const string AcmeLabel = "b2000000-0000-0000-0000-000000000001";
    private const string GlobexRepo = "30000000-0000-0000-0000-000000000099";
    private const string GlobexIssue = "50000000-0000-0000-0000-000000000099";
    private const string AcmeConnection = "a0000000-0000-0000-0000-000000000001";
    private const string GlobexConnection = "a0000000-0000-0000-0000-000000000002";

    private readonly WebApplicationFactory<Program> _factory;

    public SyncSchemaTests(WebApplicationFactory<Program> factory) => _factory = factory;

    public static TheoryData<string> AppWrites => new()
    {
        { $"insert into milestones (tenant_id, repository_id, github_milestone_id, number, title, state) values ('{Acme}', '{AcmeRepo}', 1, 9, 't', 'open')" },
        { "update milestones set title = 'x'" },
        { "delete from milestones" },
        { $"insert into labels (tenant_id, repository_id, name) values ('{Acme}', '{AcmeRepo}', 'x')" },
        { "update labels set color = 'x'" },
        { "delete from labels" },
        { $"insert into issue_labels (tenant_id, issue_id, label_id) values ('{Acme}', '{AcmeIssue}', '{AcmeLabel}')" },
        { "delete from issue_labels" },
        { $"insert into github_users (tenant_id, github_user_id, login) values ('{Acme}', 1, 'x')" },
        { "update github_users set login = 'x'" },
        { "delete from github_users" },
        { "select * from sync_runs" },
        { $"insert into sync_runs (tenant_id, connection_id) values ('{Acme}', '{AcmeConnection}')" },
        { "update repositories set sync_enabled = false" },
        { "update repositories set sync_cursor = '{}'" },
    };

    public static TheoryData<string, string> CrossTenantInserts => new()
    {
        { "milestones.repository", $"insert into milestones (tenant_id, repository_id, github_milestone_id, number, title, state) values ('{Globex}', '{AcmeRepo}', 1, 9, 't', 'open')" },
        { "labels.repository", $"insert into labels (tenant_id, repository_id, name) values ('{Globex}', '{AcmeRepo}', 't')" },
        { "issue_labels.issue", $"insert into labels (id, tenant_id, repository_id, name) values ('b2000000-0000-0000-0000-0000000000f9', '{Globex}', '{GlobexRepo}', 'g'); insert into issue_labels (tenant_id, issue_id, label_id) values ('{Globex}', '{AcmeIssue}', 'b2000000-0000-0000-0000-0000000000f9')" },
        { "issue_labels.label", $"insert into issue_labels (tenant_id, issue_id, label_id) values ('{Globex}', '{GlobexIssue}', '{AcmeLabel}')" },
        { "issues.milestone", $"update issues set milestone_id = '{AcmeMilestone}' where id = '{GlobexIssue}'" },
        { "sync_runs.connection", $"insert into sync_runs (tenant_id, connection_id) values ('{Globex}', '{AcmeConnection}')" },
        { "sync_runs.repository", $"insert into sync_runs (tenant_id, connection_id, repository_id) values ('{Globex}', '{GlobexConnection}', '{AcmeRepo}')" },
    };

    private async Task<(NpgsqlConnection Conn, NpgsqlTransaction Tx)> OpenAsync(string tenantId, string key)
    {
        var conn = await _factory.Services.GetRequiredKeyedService<NpgsqlDataSource>(key).OpenConnectionAsync();
        var tx = await conn.BeginTransactionAsync();
        await conn.ExecuteAsync("select set_config('app.tenant_id', @tenantId, true)", new { tenantId }, tx);
        return (conn, tx);
    }

    [DbTheory]
    [MemberData(nameof(AppWrites))]
    public async Task App_role_cannot_write_sync_tables_or_read_the_run_log(string sql)
    {
        var (conn, tx) = await OpenAsync(Acme, DataKeys.App);
        await using var _ = conn;

        var ex = await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(sql, transaction: tx));

        Assert.Equal("42501", ex.SqlState);
    }

    [DbTheory]
    [MemberData(nameof(CrossTenantInserts))]
    public async Task Cross_tenant_reference_is_rejected(string name, string sql)
    {
        var (conn, tx) = await OpenAsync(Globex, DataKeys.Sync);
        await using var _ = conn;

        var ex = await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(sql, transaction: tx));

        Assert.Equal("23503", ex.SqlState);
    }

    [DbTheory]
    [InlineData("milestones")]
    [InlineData("labels")]
    [InlineData("issue_labels")]
    [InlineData("github_users")]
    public async Task App_role_sees_only_its_own_tenants_rows(string table)
    {
        var (acme, acmeTx) = await OpenAsync(Acme, DataKeys.App);
        await using var _a = acme;
        var (globex, globexTx) = await OpenAsync(Globex, DataKeys.App);
        await using var _g = globex;

        Assert.True(await acme.ExecuteScalarAsync<long>($"select count(*) from {table}", transaction: acmeTx) > 0);
        Assert.Equal(0, await globex.ExecuteScalarAsync<long>($"select count(*) from {table}", transaction: globexTx));
    }

    [DbFact]
    public async Task Sync_role_writes_a_full_sync_graph_for_one_tenant()
    {
        var (conn, tx) = await OpenAsync(Acme, DataKeys.Sync);
        await using var _ = conn;

        await conn.ExecuteAsync($"""
            insert into milestones (id, tenant_id, repository_id, github_milestone_id, number, title, state)
              values ('b1000000-0000-0000-0000-0000000000f1', '{Acme}', '{AcmeRepo}', 88001, 77, 'm', 'open');
            update issues set milestone_id = 'b1000000-0000-0000-0000-0000000000f1', state_reason = 'completed' where id = '{AcmeIssue}';
            insert into issue_labels (tenant_id, issue_id, label_id) values ('{Acme}', '{AcmeIssue}', '{AcmeLabel}');
            insert into sync_runs (tenant_id, connection_id, repository_id, status) values ('{Acme}', '{AcmeConnection}', '{AcmeRepo}', 'ok');
            """, transaction: tx);
    }

    [DbFact]
    public async Task Repository_without_full_name_gets_owner_and_name_from_its_connection()
    {
        var (conn, tx) = await OpenAsync(Acme, DataKeys.Sync);
        await using var _ = conn;

        var fullName = await conn.ExecuteScalarAsync<string>(
            $"insert into repositories (tenant_id, connection_id, github_repo_id, name) values ('{Acme}', '{AcmeConnection}', 424242, 'new-repo') returning full_name",
            transaction: tx);

        Assert.Equal("acme-health/new-repo", fullName);
    }
}
