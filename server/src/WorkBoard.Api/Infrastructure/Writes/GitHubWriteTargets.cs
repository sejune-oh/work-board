using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.GitHub;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Infrastructure.Writes;

public sealed record GitHubWriteTarget(Guid RepositoryId, GitHubRepoRef Repo, bool Archived);

public interface IGitHubWriteTargets
{
    // Null unless the repository belongs to the caller's tenant and to an active connection that has an App installation.
    Task<GitHubWriteTarget?> ResolveAsync(Guid repositoryId, CancellationToken ct);
}

public sealed class GitHubWriteTargets(TenantDb db, ICurrentUser user) : IGitHubWriteTargets
{
    private sealed record Row(string FullName, long InstallationId, bool Archived);

    public async Task<GitHubWriteTarget?> ResolveAsync(Guid repositoryId, CancellationToken ct)
    {
        var tenantId = user.Identity.TenantId;
        var row = await db.ReadAsync(session => session.QuerySingleOrDefaultAsync<Row>("""
            select r.full_name as FullName, c.installation_id as InstallationId, r.archived as Archived
              from repositories r
              join github_connections c on c.tenant_id = r.tenant_id and c.id = r.connection_id
             where r.tenant_id = @tenantId and r.id = @repositoryId
               and c.disconnected_at is null and c.installation_id is not null
            """, new { tenantId, repositoryId }), ct);
        if (row is null)
            return null;
        var slash = row.FullName.IndexOf('/');
        return slash <= 0
            ? null
            : new GitHubWriteTarget(repositoryId,
                new GitHubRepoRef(row.InstallationId, row.FullName[..slash], row.FullName[(slash + 1)..]), row.Archived);
    }
}
