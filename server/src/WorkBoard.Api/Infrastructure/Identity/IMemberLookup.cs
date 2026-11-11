namespace WorkBoard.Api.Infrastructure.Identity;

public interface IMemberLookup
{
    Task<IReadOnlyList<DevIdentity>> FindByUserIdAsync(long githubUserId, CancellationToken ct);

    // Null when the login matches no member, or members with different user ids.
    Task<long?> FindUserIdByLoginAsync(string githubLogin, CancellationToken ct);

    Task<IReadOnlyList<DevUser>> ListAsync(CancellationToken ct);
}
