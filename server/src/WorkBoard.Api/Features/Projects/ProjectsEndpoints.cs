using WorkBoard.Api.Infrastructure.Data;

namespace WorkBoard.Api.Features.Projects;

public sealed record RepositoryRef(Guid Id, string Name);

public sealed record Project(Guid Id, string Name, string? Color, IReadOnlyList<RepositoryRef> Repositories);

public static class ProjectsEndpoints
{
    public static void MapProjects(this RouteGroupBuilder api) =>
        api.MapGet("/projects", async (TenantDb db, CancellationToken ct) =>
        {
            var (projects, repos) = await db.ReadAsync(async s => (
                await s.QueryAsync<ProjectRow>("select id, name, color from projects order by name"),
                await s.QueryAsync<ProjectRepoRow>("""
                    select pr.project_id, r.id as repository_id, r.name
                      from project_repositories pr
                      join repositories r on r.id = pr.repository_id
                     order by r.name
                    """)), ct);

            var byProject = repos.ToLookup(r => r.ProjectId, r => new RepositoryRef(r.RepositoryId, r.Name));
            return projects.Select(p => new Project(p.Id, p.Name, p.Color, byProject[p.Id].ToList())).ToList();
        });

    private sealed class ProjectRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string? Color { get; set; }
    }

    private sealed class ProjectRepoRow
    {
        public Guid ProjectId { get; set; }
        public Guid RepositoryId { get; set; }
        public string Name { get; set; } = "";
    }
}
