namespace WorkBoard.Api.Infrastructure.Identity;

public interface IMemberLookup
{
    Task<IReadOnlyList<DevIdentity>> FindByLoginAsync(string githubLogin, CancellationToken ct);

    Task<IReadOnlyList<DevUser>> ListAsync(CancellationToken ct);
}
