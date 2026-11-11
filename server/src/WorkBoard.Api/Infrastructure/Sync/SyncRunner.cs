using System.Collections.Concurrent;
using System.Text.Json;
using Dapper;
using Npgsql;
using WorkBoard.Api.Infrastructure.GitHub;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Infrastructure.Sync;

// Runs syncs in the background, at most one per tenant at a time (in this process). Every run is
// recorded in sync_runs; failures are recorded and never thrown.
public sealed class SyncRunner(
    IServiceScopeFactory scopes,
    [FromKeyedServices(DataKeys.Sync)] NpgsqlDataSource sync,
    IHostApplicationLifetime lifetime,
    TimeProvider time,
    ILogger<SyncRunner> logger)
{
    public static readonly TimeSpan Overlap = TimeSpan.FromMinutes(2);
    private const int MaxErrorLength = 300;

    private readonly ConcurrentDictionary<Guid, Task> _running = new();

    // Set by SyncWorker; null while the periodic loop is off.
    public DateTimeOffset? NextRunAt { get; set; }

    public bool IsRunning(Guid tenantId) => _running.ContainsKey(tenantId);

    // Null when a run for the tenant is already in progress.
    public Task? TryStart(Guid tenantId)
    {
        var done = new TaskCompletionSource();
        if (!_running.TryAdd(tenantId, done.Task))
            return null;

        return Task.Run(async () =>
        {
            try
            {
                await RunTenantAsync(tenantId, lifetime.ApplicationStopping);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Sync for tenant {TenantId} crashed", tenantId);
            }
            finally
            {
                _running.TryRemove(tenantId, out _);
                done.SetResult();
            }
        });
    }

    public async Task FailInterruptedRunsAsync(CancellationToken ct)
    {
        await using var conn = await sync.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "update sync_runs set status = 'failed', finished_at = now(), error = 'interrupted' where status = 'running'",
            cancellationToken: ct));
    }

    private sealed record ConnectionRow(Guid Id, long InstallationId);

    private sealed record RepoRow(Guid Id, string FullName, DateTime? LastSyncedAt);

    private async Task RunTenantAsync(Guid tenantId, CancellationToken ct)
    {
        List<ConnectionRow> connections;
        await using (var conn = await sync.OpenConnectionAsync(ct))
        {
            connections = (await conn.QueryAsync<ConnectionRow>(new CommandDefinition("""
                select id, installation_id from github_connections
                 where tenant_id = @tenantId and disconnected_at is null and installation_id is not null
                 order by created_at
                """, new { tenantId }, cancellationToken: ct))).AsList();
        }

        foreach (var connection in connections)
            await RunConnectionAsync(tenantId, connection, ct);
    }

    private async Task RunConnectionAsync(Guid tenantId, ConnectionRow connection, CancellationToken ct)
    {
        var runId = await StartRunAsync(tenantId, connection.Id);
        var (issues, pullRequests, comments) = (0, 0, 0);
        int? remaining = null;
        var errors = new List<string>();

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var source = scope.ServiceProvider.GetRequiredService<IGitHubSource>();
            var writer = scope.ServiceProvider.GetRequiredService<MirrorWriter>();

            var list = await source.ListRepositoriesAsync(connection.InstallationId, ct);
            remaining = list.RateLimit?.Remaining ?? remaining;
            await writer.UpsertRepositoriesAsync(tenantId, connection.Id, list.Repositories, ct);

            var repos = await EnabledRepositoriesAsync(tenantId, connection.Id, list.Repositories.Select(r => r.Id).ToArray(), ct);
            foreach (var repo in repos)
            {
                var startedAt = time.GetUtcNow();
                var slash = repo.FullName.IndexOf('/');
                DateTimeOffset? since = repo.LastSyncedAt is { } last
                    ? new DateTimeOffset(DateTime.SpecifyKind(last, DateTimeKind.Utc)) - Overlap
                    : null;
                try
                {
                    var snapshot = await source.ReadRepositoryAsync(
                        connection.InstallationId, repo.FullName[..slash], repo.FullName[(slash + 1)..], since, ct);
                    remaining = snapshot.RateLimit?.Remaining ?? remaining;
                    var result = await writer.ApplyAsync(tenantId, connection.Id, snapshot, ct);
                    issues += result.Issues;
                    pullRequests += result.PullRequests;
                    comments += result.Comments;
                    await MarkSyncedAsync(tenantId, repo.Id, startedAt, since, result);
                }
                catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    logger.LogWarning(e, "Sync of {Repository} failed", repo.FullName);
                    errors.Add($"{repo.FullName}: {Describe(e)}");
                    if (e is GitHubSourceException { Kind: GitHubSourceErrorKind.RateLimited or GitHubSourceErrorKind.Unauthorized })
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            errors.Add("interrupted");
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Sync of connection {ConnectionId} failed", connection.Id);
            errors.Add(Describe(e));
        }

        await FinishRunAsync(runId, errors, issues, pullRequests, comments, remaining);
    }

    private static string Describe(Exception e) => e switch
    {
        GitHubSourceException g => $"{g.Kind}: {g.Message}",
        GitHubAppKeyException g => g.Message,
        HttpRequestException or TaskCanceledException => "github_unavailable",
        _ => "unexpected_error",
    };

    private async Task<Guid> StartRunAsync(Guid tenantId, Guid connectionId)
    {
        await using var conn = await sync.OpenConnectionAsync();
        return await conn.QuerySingleAsync<Guid>(
            "insert into sync_runs (tenant_id, connection_id) values (@tenantId, @connectionId) returning id",
            new { tenantId, connectionId });
    }

    private async Task<List<RepoRow>> EnabledRepositoriesAsync(Guid tenantId, Guid connectionId, long[] githubIds, CancellationToken ct)
    {
        await using var conn = await sync.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<RepoRow>(new CommandDefinition("""
            select id, full_name, last_synced_at from repositories
             where tenant_id = @tenantId and connection_id = @connectionId and sync_enabled and github_repo_id = any(@githubIds)
             order by full_name
            """, new { tenantId, connectionId, githubIds }, cancellationToken: ct))).AsList();
    }

    private async Task MarkSyncedAsync(Guid tenantId, Guid repositoryId, DateTimeOffset startedAt, DateTimeOffset? since, MirrorResult result)
    {
        var cursor = JsonSerializer.Serialize(new
        {
            since,
            syncedAt = startedAt,
            result.Issues,
            result.PullRequests,
            result.Comments,
        });
        await using var conn = await sync.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update repositories set last_synced_at = @syncedAt, sync_cursor = @cursor::jsonb where tenant_id = @tenantId and id = @repositoryId",
            new { tenantId, repositoryId, syncedAt = startedAt.UtcDateTime, cursor });
    }

    private async Task FinishRunAsync(
        Guid runId, List<string> errors, int issues, int pullRequests, int comments, int? remaining)
    {
        var error = errors.Count == 0 ? null : string.Join("; ", errors);
        if (error is { Length: > MaxErrorLength })
            error = error[..MaxErrorLength];
        await using var conn = await sync.OpenConnectionAsync();
        await conn.ExecuteAsync("""
            update sync_runs
               set finished_at = now(), status = @status, issues_synced = @issues, pull_requests_synced = @pullRequests,
                   comments_synced = @comments, rate_limit_remaining = @remaining, error = @error
             where id = @runId
            """, new
        {
            runId, status = error is null ? "ok" : "failed", issues, pullRequests, comments, remaining, error,
        });
    }
}
