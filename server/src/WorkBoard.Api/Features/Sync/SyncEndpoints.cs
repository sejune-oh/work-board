using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using WorkBoard.Api.Infrastructure.GitHub;
using WorkBoard.Api.Infrastructure.Identity;
using WorkBoard.Api.Infrastructure.Sync;

namespace WorkBoard.Api.Features.Sync;

public sealed record SyncStatus(
    bool Connected,
    bool Running,
    DateTime? LastSyncedAt,
    string? LastResult,
    string? LastError,
    int? RateLimitRemaining,
    DateTimeOffset? NextRunAt);

public sealed record SyncStarted(bool Started);

public static class SyncEndpoints
{
    private sealed record LastRun(string Status, string? Error, int? RateLimitRemaining);

    public static void MapSync(this RouteGroupBuilder api)
    {
        api.MapGet("/sync/status", StatusAsync);
        api.MapPost("/sync/run", RunAsync);
    }

    // sync_runs is not readable by the app role, so these reads go through the sync role with an explicit tenant filter.
    private static async Task<IResult> StatusAsync(
        ICurrentUser user, SyncRunner runner, [FromKeyedServices(DataKeys.Sync)] NpgsqlDataSource sync, CancellationToken ct)
    {
        var tenantId = user.Identity.TenantId;
        await using var conn = await sync.OpenConnectionAsync(ct);
        if (!await ConnectedAsync(conn, tenantId, ct))
            return Results.Ok(new SyncStatus(false, false, null, null, null, null, null));

        var lastSyncedAt = await conn.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            "select max(last_synced_at) from repositories where tenant_id = @tenantId", new { tenantId }, cancellationToken: ct));
        var last = await conn.QuerySingleOrDefaultAsync<LastRun>(new CommandDefinition("""
            select status, error, rate_limit_remaining from sync_runs
             where tenant_id = @tenantId and finished_at is not null
             order by started_at desc limit 1
            """, new { tenantId }, cancellationToken: ct));

        return Results.Ok(new SyncStatus(
            true, runner.IsRunning(tenantId), lastSyncedAt, last?.Status, last?.Error, last?.RateLimitRemaining, runner.NextRunAt));
    }

    private static async Task<IResult> RunAsync(
        ICurrentUser user, IOptions<GitHubAppOptions> app, SyncRunner runner,
        [FromKeyedServices(DataKeys.Sync)] NpgsqlDataSource sync, CancellationToken ct)
    {
        var caller = user.Identity;
        if (caller.Member.Role != "admin")
            return Error(StatusCodes.Status403Forbidden, "admin_only");
        if (!app.Value.CanReadInstallations)
            return Error(StatusCodes.Status503ServiceUnavailable, "github_app_not_configured");

        await using (var conn = await sync.OpenConnectionAsync(ct))
        {
            if (!await ConnectedAsync(conn, caller.TenantId, ct))
                return Error(StatusCodes.Status409Conflict, "not_connected");
        }

        return runner.TryStart(caller.TenantId) is null
            ? Error(StatusCodes.Status409Conflict, "sync_running")
            : Results.Json(new SyncStarted(true), statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<bool> ConnectedAsync(NpgsqlConnection conn, Guid tenantId, CancellationToken ct) =>
        await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
            select exists (select 1 from github_connections
                            where tenant_id = @tenantId and disconnected_at is null and installation_id is not null)
            """, new { tenantId }, cancellationToken: ct));

    private static IResult Error(int status, string code) => Results.Json(new ErrorBody(code), statusCode: status);
}
