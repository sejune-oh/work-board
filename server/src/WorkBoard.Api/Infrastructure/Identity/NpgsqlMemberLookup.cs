using Dapper;
using Npgsql;
using WorkBoard.Api.Features.Members;

namespace WorkBoard.Api.Infrastructure.Identity;

// Reads members across all tenants on the sync (RLS-bypassing) connection. Identity only, never tenant data.
public sealed class NpgsqlMemberLookup([FromKeyedServices(DataKeys.Sync)] NpgsqlDataSource syncSource) : IMemberLookup
{
    public async Task<long?> FindUserIdByLoginAsync(string githubLogin, CancellationToken ct)
    {
        await using var conn = await syncSource.OpenConnectionAsync(ct);
        var ids = (await conn.QueryAsync<long>(new CommandDefinition(
            "select distinct github_user_id from members where github_login = @githubLogin limit 2",
            new { githubLogin }, cancellationToken: ct))).ToList();
        return ids.Count == 1 ? ids[0] : null;
    }

    public async Task<IReadOnlyList<DevIdentity>> FindByUserIdAsync(long githubUserId, CancellationToken ct)
    {
        await using var conn = await syncSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<IdentityRow>(new CommandDefinition("""
            select m.id, m.github_user_id, m.github_login, m.display_name, m.avatar_url, m.role,
                   m.tenant_id, t.name as tenant_name
              from members m
              join tenants t on t.id = m.tenant_id
             where m.github_user_id = @githubUserId
             order by t.name
            """, new { githubUserId }, cancellationToken: ct));

        return rows
            .Select(row => new DevIdentity(
                new Member(row.Id, row.GithubUserId, row.GithubLogin, row.DisplayName, row.AvatarUrl, row.Role),
                row.TenantId,
                row.TenantName))
            .ToList();
    }

    public async Task<IReadOnlyList<DevUser>> ListAsync(CancellationToken ct)
    {
        await using var conn = await syncSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<DevUserRow>(new CommandDefinition("""
            select m.github_login, m.github_user_id, m.display_name, m.tenant_id, t.name as tenant_name
              from members m
              join tenants t on t.id = m.tenant_id
             order by t.name, m.github_login
            """, cancellationToken: ct));
        return rows.Select(r => new DevUser(r.GithubLogin, r.GithubUserId, r.DisplayName, r.TenantId, r.TenantName)).ToList();
    }

    private sealed class IdentityRow
    {
        public Guid Id { get; set; }
        public long GithubUserId { get; set; }
        public string GithubLogin { get; set; } = "";
        public string? DisplayName { get; set; }
        public string? AvatarUrl { get; set; }
        public string Role { get; set; } = "member";
        public Guid TenantId { get; set; }
        public string TenantName { get; set; } = "";
    }

    private sealed class DevUserRow
    {
        public string GithubLogin { get; set; } = "";
        public long GithubUserId { get; set; }
        public Guid TenantId { get; set; }
        public string? DisplayName { get; set; }
        public string TenantName { get; set; } = "";
    }
}
