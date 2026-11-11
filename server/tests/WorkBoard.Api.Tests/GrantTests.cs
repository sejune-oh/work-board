using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Tests;

public class GrantTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Acme = "10000000-0000-0000-0000-000000000001";

    private readonly WebApplicationFactory<Program> _factory;

    public GrantTests(WebApplicationFactory<Program> factory) => _factory = factory;

    public static TheoryData<string, string> Denied => new()
    {
        { "tenants", "update tenants set name = 'x'" },
        { "github_connections", "update github_connections set account_login = 'x'" },
        { "members", "update members set display_name = 'x'" },
        { "members", "delete from members" },
        { "repositories", "update repositories set archived = true" },
        { "issues", "update issues set title = 'x'" },
        { "issues", "delete from issues" },
        { "issue_assignees", "delete from issue_assignees" },
        { "pull_requests", "update pull_requests set title = 'x'" },
        { "issue_pull_requests", "delete from issue_pull_requests" },
        { "comments", "delete from comments" },
        { "webhook_deliveries", "select * from webhook_deliveries" },
        { "ai_usage", "select * from ai_usage" },
        { "auth_links", "select * from auth_links" },
        { "auth_links", "insert into auth_links (auth_user_id, github_user_id) values (gen_random_uuid(), 1)" },
        { "auth_links", "update auth_links set github_user_id = 1" },
        { "auth_links", "delete from auth_links" },
        { "ai_usage", "insert into ai_usage (tenant_id, feature) values ('10000000-0000-0000-0000-000000000001', 'x')" },
    };

    public static TheoryData<string> Writable => new()
    {
        "update tenant_settings set size_enabled = size_enabled",
        "update projects set name = name",
        "update project_repositories set project_id = project_id",
        "update sprints set name = name",
        "update board_fields set size = size",
        "update meeting_notes set title = title",
        "update meeting_attendees set note_id = note_id",
        "update meeting_action_items set text = text",
    };

    private async Task<(NpgsqlConnection Conn, NpgsqlTransaction Tx)> OpenAsync()
    {
        var source = _factory.Services.GetRequiredKeyedService<NpgsqlDataSource>(DataKeys.App);
        var conn = await source.OpenConnectionAsync();
        var tx = await conn.BeginTransactionAsync();
        await conn.ExecuteAsync("select set_config('app.tenant_id', @tenantId, true)", new { tenantId = Acme }, tx);
        return (conn, tx);
    }

    [DbTheory]
    [MemberData(nameof(Denied))]
    public async Task App_role_cannot_touch_sync_owned_tables(string table, string sql)
    {
        var (conn, tx) = await OpenAsync();
        await using var _ = conn;

        var ex = await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(sql, transaction: tx));

        Assert.Equal("42501", ex.SqlState);
    }

    [DbTheory]
    [MemberData(nameof(Writable))]
    public async Task App_role_can_write_board_only_tables(string sql)
    {
        var (conn, tx) = await OpenAsync();
        await using var _ = conn;

        await conn.ExecuteAsync(sql, transaction: tx);
    }

    [DbFact]
    public async Task App_role_can_still_read_everything_the_api_serves()
    {
        var (conn, tx) = await OpenAsync();
        await using var _ = conn;

        foreach (var table in new[] { "tenants", "members", "repositories", "issues", "issue_assignees",
                     "pull_requests", "issue_pull_requests", "comments", "github_connections", "issue_status", "board_fields" })
            await conn.ExecuteScalarAsync<long>($"select count(*) from {table}", transaction: tx);
    }
}
