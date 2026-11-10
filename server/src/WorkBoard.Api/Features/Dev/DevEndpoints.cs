using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Features.Dev;

public static class DevEndpoints
{
    public static void MapDevUsers(this RouteGroupBuilder api) =>
        api.MapGet("/dev/users", (IMemberLookup members, CancellationToken ct) => members.ListAsync(ct));
}
