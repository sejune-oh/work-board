namespace WorkBoard.Api.Infrastructure.Identity;

public interface IMemberLookup
{
    Task<DevIdentity?> FindByLoginAsync(string githubLogin, CancellationToken ct);

    Task<IReadOnlyList<DevUser>> ListAsync(CancellationToken ct);
}
