using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace WorkBoard.Api.Infrastructure.Identity;

public interface IAuthLinkStore
{
    Task<long?> FindAsync(Guid authUserId, CancellationToken ct);

    Task SaveAsync(Guid authUserId, long githubUserId, CancellationToken ct);
}

// auth_links is global and only the sync role can reach it.
public sealed class NpgsqlAuthLinkStore([FromKeyedServices(DataKeys.Sync)] NpgsqlDataSource syncSource) : IAuthLinkStore
{
    public async Task<long?> FindAsync(Guid authUserId, CancellationToken ct)
    {
        await using var conn = await syncSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
            "select github_user_id from auth_links where auth_user_id = @authUserId", new { authUserId }, cancellationToken: ct));
    }

    public async Task SaveAsync(Guid authUserId, long githubUserId, CancellationToken ct)
    {
        await using var conn = await syncSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "insert into auth_links (auth_user_id, github_user_id) values (@authUserId, @githubUserId) on conflict (auth_user_id) do nothing",
            new { authUserId, githubUserId }, cancellationToken: ct));
    }
}

public interface IAuthLinkResolver
{
    Task<long?> ResolveGithubUserIdAsync(Guid authUserId, CancellationToken ct);
}

// Memory, then the stored link, then the identity source (stored on first sight). Misses are not cached.
public sealed class AuthLinkResolver(IAuthLinkStore store, IAuthIdentitySource source, IMemoryCache cache) : IAuthLinkResolver
{
    private static readonly TimeSpan Sliding = TimeSpan.FromMinutes(30);

    public async Task<long?> ResolveGithubUserIdAsync(Guid authUserId, CancellationToken ct)
    {
        if (cache.TryGetValue(authUserId, out long cached))
            return cached;

        var githubUserId = await store.FindAsync(authUserId, ct);
        if (githubUserId is null)
        {
            githubUserId = (await source.FindGithubProfileAsync(authUserId, ct))?.Id;
            if (githubUserId is null)
                return null;
            await store.SaveAsync(authUserId, githubUserId.Value, ct);
        }

        cache.Set(authUserId, githubUserId.Value, new MemoryCacheEntryOptions { SlidingExpiration = Sliding });
        return githubUserId;
    }
}
