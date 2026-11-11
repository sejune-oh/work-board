using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Features.Issues;

public static class IssuesEndpoints
{
    private const int DefaultDoneWithinDays = 7;
    private const int MaxDoneWithinDays = 365;
    private static readonly HashSet<string> ValidStatuses = ["todo", "in_progress", "in_review", "done", "cancelled"];

    public static void MapIssues(this RouteGroupBuilder api)
    {
        api.MapGet("/issues", async (
            Guid? projectId, string? assignee, string? person, int? doneWithinDays, string? status, bool? stale,
            Guid? sprint, string? scope, string? doneSince, TenantDb db, CancellationToken ct) =>
        {
            if (doneWithinDays is < 0 or > MaxDoneWithinDays)
                return Results.Json(new ErrorBody("invalid_doneWithinDays"), statusCode: StatusCodes.Status400BadRequest);

            string[]? statuses = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                statuses = status.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Distinct().ToArray();
                if (statuses.Length == 0 || statuses.Any(x => !ValidStatuses.Contains(x)))
                    return Results.Json(new ErrorBody("invalid_status"), statusCode: StatusCodes.Status400BadRequest);
            }

            if (!string.IsNullOrEmpty(scope) && scope != "all")
                return Results.Json(new ErrorBody("invalid_scope"), statusCode: StatusCodes.Status400BadRequest);

            if (!string.IsNullOrEmpty(doneSince) && doneSince != "week")
                return Results.Json(new ErrorBody("invalid_doneSince"), statusCode: StatusCodes.Status400BadRequest);

            var login = string.IsNullOrWhiteSpace(person) ? assignee : person;
            var filter = new IssueFilter(
                projectId,
                string.IsNullOrWhiteSpace(login) ? null : login.Trim(),
                doneWithinDays ?? DefaultDoneWithinDays,
                statuses,
                stale ?? false,
                sprint,
                scope == "all",
                doneSince == "week");
            return Results.Ok(await db.ReadAsync(s => IssueQueries.ListAsync(s, filter), ct));
        });

        api.MapGet("/issues/{id:guid}", async (Guid id, TenantDb db, CancellationToken ct) =>
        {
            var detail = await db.ReadAsync(s => IssueDetailQueries.GetAsync(s, id), ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });
    }
}
