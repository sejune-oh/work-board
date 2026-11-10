using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Tests;

public class TenantIntegrityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Acme = "10000000-0000-0000-0000-000000000001";
    private const string Globex = "10000000-0000-0000-0000-000000000002";
    private const string AcmeIssue = "50000000-0000-0000-0000-000000000001";
    private const string AcmeSprint = "70000000-0000-0000-0000-000000000001";
    private const string AcmeProject = "40000000-0000-0000-0000-000000000001";
    private const string AcmeMember = "20000000-0000-0000-0000-000000000001";
    private const string AcmeRepo = "30000000-0000-0000-0000-000000000001";
    private const string AcmePr = "60000000-0000-0000-0000-000000000001";
    private const string GlobexIssue = "50000000-0000-0000-0000-000000000099";
    private const string GlobexRepo = "30000000-0000-0000-0000-000000000099";

    private readonly WebApplicationFactory<Program> _factory;

    public TenantIntegrityTests(WebApplicationFactory<Program> factory) => _factory = factory;

    public static TheoryData<string, string> CrossTenantInserts => new()
    {
        { "board_fields", $"insert into board_fields (issue_id, tenant_id) values ('{AcmeIssue}', '{Globex}')" },
        { "board_fields.sprint", $"insert into board_fields (issue_id, tenant_id, sprint_id) values ('{GlobexIssue}', '{Globex}', '{AcmeSprint}')" },
        { "issue_assignees", $"insert into issue_assignees (tenant_id, issue_id, github_user_id) values ('{Globex}', '{AcmeIssue}', 1)" },
        { "comments", $"insert into comments (tenant_id, issue_id, github_comment_id, body, github_created_at, github_updated_at) values ('{Globex}', '{AcmeIssue}', 1, 'x', now(), now())" },
        { "issue_pull_requests", $"insert into issue_pull_requests (tenant_id, issue_id, pull_request_id) values ('{Globex}', '{GlobexIssue}', '{AcmePr}')" },
        { "project_repositories", $"insert into project_repositories (tenant_id, project_id, repository_id) values ('{Globex}', '{AcmeProject}', '30000000-0000-0000-0000-000000000003')" },
        { "meeting_notes.project", $"insert into meeting_notes (tenant_id, project_id, title, held_at) values ('{Globex}', '{AcmeProject}', 't', now())" },
        { "meeting_notes.created_by", $"insert into meeting_notes (tenant_id, created_by, title, held_at) values ('{Globex}', '{AcmeMember}', 't', now())" },
        { "issues.repository", $"insert into issues (tenant_id, repository_id, github_issue_id, number, title, state, github_created_at, github_updated_at) values ('{Globex}', '{AcmeRepo}', 1, 1, 't', 'open', now(), now())" },
        { "issues.parent", $"insert into issues (tenant_id, repository_id, github_issue_id, number, title, state, parent_issue_id, github_created_at, github_updated_at) values ('{Globex}', '{GlobexRepo}', 2, 2, 't', 'open', '{AcmeIssue}', now(), now())" },
        { "pull_requests.repository", $"insert into pull_requests (tenant_id, repository_id, github_pr_id, number, title, state, github_updated_at) values ('{Globex}', '{AcmeRepo}', 1, 1, 't', 'open', now())" },
    };

    // Disposing the connection without commit rolls the transaction back.
    private async Task<(NpgsqlConnection Conn, NpgsqlTransaction Tx)> OpenAsTenantAsync(string tenantId)
    {
        var source = _factory.Services.GetRequiredKeyedService<NpgsqlDataSource>(DataKeys.App);
        var conn = await source.OpenConnectionAsync();
        var tx = await conn.BeginTransactionAsync();
        await conn.ExecuteAsync("select set_config('app.tenant_id', @tenantId, true)", new { tenantId }, tx);
        return (conn, tx);
    }

    [DbTheory]
    [MemberData(nameof(CrossTenantInserts))]
    public async Task Cross_tenant_reference_is_rejected(string name, string sql)
    {
        var (conn, tx) = await OpenAsTenantAsync(Globex);
        await using var _ = conn;

        var ex = await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(sql, transaction: tx));

        Assert.Equal("23503", ex.SqlState);
    }

    [DbFact]
    public async Task Same_tenant_reference_is_accepted()
    {
        var (conn, tx) = await OpenAsTenantAsync(Acme);
        await using var _ = conn;

        var rows = await conn.ExecuteAsync(
            $"insert into board_fields (issue_id, tenant_id, sprint_id) values ('{AcmeIssue}', '{Acme}', '{AcmeSprint}')", transaction: tx);

        Assert.Equal(1, rows);
    }
}
