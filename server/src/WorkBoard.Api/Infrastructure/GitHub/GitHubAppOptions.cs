namespace WorkBoard.Api.Infrastructure.GitHub;

public sealed class GitHubAppOptions
{
    public const string Section = "GitHub:App";

    public long AppId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    // Absolute, or relative to the API content root.
    public string? PrivateKeyPath { get; set; }
    public string? Slug { get; set; }

    public bool Configured =>
        AppId > 0
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(PrivateKeyPath);
}
