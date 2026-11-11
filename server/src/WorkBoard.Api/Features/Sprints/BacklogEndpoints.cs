using WorkBoard.Api.Features.Issues;
using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.Writes;

namespace WorkBoard.Api.Features.Sprints;

public static class BacklogEndpoints
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    private static readonly HashSet<string> ValidStatuses = ["todo", "in_progress", "in_review"];

    public static void MapBacklog(this RouteGroupBuilder api) =>
        api.MapGet("/backlog", async (
            Guid? projectId, string? assignee, string? person, string? status, int? page, int? pageSize,
            TenantDb db, CancellationToken ct) =>
        {
            string[]? statuses = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                statuses = status.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Distinct().ToArray();
                if (statuses.Length == 0 || statuses.Any(x => !ValidStatuses.Contains(x)))
                    return ApiErrors.BadRequest("invalid_status");
            }
            if (page is < 1 || pageSize is < 1 or > MaxPageSize)
                return ApiErrors.BadRequest("invalid_paging");

            var login = string.IsNullOrWhiteSpace(person) ? assignee : person;
            var filter = new BacklogFilter(
                projectId, string.IsNullOrWhiteSpace(login) ? null : login.Trim(), statuses,
                page ?? 1, pageSize ?? DefaultPageSize);
            return Results.Ok(await db.ReadAsync(async s =>
                await SprintQueries.EnabledAsync(s)
                    ? await IssueQueries.BacklogAsync(s, filter)
                    : new BacklogPage([], 0, filter.Page, filter.PageSize), ct));
        });
}
