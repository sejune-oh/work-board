using WorkBoard.Api.Infrastructure.Data;

namespace WorkBoard.Api.Features.Overview;

public static class OverviewEndpoints
{
    public static void MapOverview(this RouteGroupBuilder api) =>
        api.MapGet("/overview", async (TenantDb db, CancellationToken ct) =>
            Results.Ok(await db.ReadAsync(OverviewQueries.BuildAsync, ct)));
}
