namespace WorkBoard.Api.Features.Health;

public static class HealthEndpoints
{
    public static void MapHealth(this RouteGroupBuilder api) =>
        api.MapGet("/health", () => new HealthResponse("ok"));

    public sealed record HealthResponse(string Status);
}
