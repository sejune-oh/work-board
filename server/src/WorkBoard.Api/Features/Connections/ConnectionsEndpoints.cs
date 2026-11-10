using WorkBoard.Api.Infrastructure.Data;

namespace WorkBoard.Api.Features.Connections;

public sealed record GithubConnection(
    Guid Id,
    long AccountId,
    string AccountLogin,
    string AccountType,
    string? RepositorySelection,
    DateTime CreatedAt,
    DateTime? DisconnectedAt);

public static class ConnectionsEndpoints
{
    public static void MapConnections(this RouteGroupBuilder api) =>
        api.MapGet("/connections", async (TenantDb db, CancellationToken ct) =>
        {
            var rows = await db.ReadAsync(
                s => s.QueryAsync<GithubConnection>("""
                    select id, account_id, account_login, account_type, repository_selection, created_at, disconnected_at
                      from github_connections
                     order by disconnected_at is not null, account_login
                    """),
                ct);
            return rows.ToList();
        });
}
