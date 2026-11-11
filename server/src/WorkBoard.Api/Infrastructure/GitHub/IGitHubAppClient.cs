namespace WorkBoard.Api.Infrastructure.GitHub;

public sealed record GitHubUser(long Id, string Login);

public sealed record GitHubInstallation(long AccountId, string AccountLogin, string AccountType, string? RepositorySelection);

public interface IGitHubAppClient
{
    // Null when GitHub rejects the code.
    Task<string?> ExchangeCodeAsync(string code, CancellationToken ct);

    // Null when GitHub rejects the user token.
    Task<GitHubUser?> GetUserAsync(string userToken, CancellationToken ct);

    Task<IReadOnlyList<long>> ListUserInstallationIdsAsync(string userToken, CancellationToken ct);

    // Authenticated as the App itself. Null when the installation does not exist.
    Task<GitHubInstallation?> GetInstallationAsync(long installationId, CancellationToken ct);
}
