using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using WorkBoard.Api.Infrastructure.GitHub;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Infrastructure.Sync;

// Syncs every connected tenant each Sync:IntervalMinutes.
public sealed class SyncWorker(
    SyncRunner runner,
    IOptions<SyncOptions> options,
    IOptions<GitHubAppOptions> app,
    [FromKeyedServices(DataKeys.Sync)] NpgsqlDataSource sync,
    TimeProvider time,
    ILogger<SyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled)
            return;

        try
        {
            await runner.FailInterruptedRunsAsync(ct);
            runner.NextRunAt = time.GetUtcNow() + StartDelay;
            await Task.Delay(StartDelay, time, ct);

            while (!ct.IsCancellationRequested)
            {
                if (app.Value.Configured)
                    await RunAllAsync(ct);
                var interval = options.Value.Interval;
                runner.NextRunAt = time.GetUtcNow() + interval;
                await Task.Delay(interval, time, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            logger.LogError(e, "Sync loop stopped");
        }
    }

    private async Task RunAllAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = await sync.OpenConnectionAsync(ct);
            var tenants = (await conn.QueryAsync<Guid>(
                "select distinct tenant_id from github_connections where disconnected_at is null and installation_id is not null")).AsList();
            foreach (var tenantId in tenants)
                if (runner.TryStart(tenantId) is { } running)
                    await running;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Sync pass failed");
        }
    }
}
