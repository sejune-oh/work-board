using WorkBoard.Api.Infrastructure.Data;

namespace WorkBoard.Api.Features.Issues;

public static class IssuesEndpoints
{
    private const int DefaultDoneWithinDays = 7;

    public static void MapIssues(this RouteGroupBuilder api)
    {
        api.MapGet("/issues", async (
            Guid? projectId, string? assignee, int? doneWithinDays, TenantDb db, CancellationToken ct) =>
        {
            var filter = new IssueFilter(
                projectId,
                string.IsNullOrWhiteSpace(assignee) ? null : assignee.Trim(),
                Math.Max(doneWithinDays ?? DefaultDoneWithinDays, 0));
            return await db.ReadAsync(s => IssueQueries.ListAsync(s, filter), ct);
        });

        api.MapGet("/issues/{id:guid}", async (Guid id, TenantDb db, CancellationToken ct) =>
        {
            var detail = await db.ReadAsync(s => IssueDetailQueries.GetAsync(s, id), ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });
    }
}
