using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Features.Issues;

public static class IssuesEndpoints
{
    private const int DefaultDoneWithinDays = 7;
    private const int MaxDoneWithinDays = 365;

    public static void MapIssues(this RouteGroupBuilder api)
    {
        api.MapGet("/issues", async (
            Guid? projectId, string? assignee, int? doneWithinDays, TenantDb db, CancellationToken ct) =>
        {
            if (doneWithinDays is < 0 or > MaxDoneWithinDays)
                return Results.Json(new ErrorBody("invalid_doneWithinDays"), statusCode: StatusCodes.Status400BadRequest);

            var filter = new IssueFilter(
                projectId,
                string.IsNullOrWhiteSpace(assignee) ? null : assignee.Trim(),
                doneWithinDays ?? DefaultDoneWithinDays);
            return Results.Ok(await db.ReadAsync(s => IssueQueries.ListAsync(s, filter), ct));
        });

        api.MapGet("/issues/{id:guid}", async (Guid id, TenantDb db, CancellationToken ct) =>
        {
            var detail = await db.ReadAsync(s => IssueDetailQueries.GetAsync(s, id), ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });
    }
}
