using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace WorkBoard.Api.Infrastructure.GitHub;

public interface IGitHubInstallationTokens
{
    Task<string> GetAsync(long installationId, CancellationToken ct);

    // Drops the cached token, e.g. after GitHub answered 401 for it.
    void Invalidate(long installationId);
}

// Installation tokens are cached in memory per installation and renewed shortly before they expire.
// They are never logged or persisted.
public sealed class GitHubInstallationTokens(
    IHttpClientFactory httpFactory,
    IOptions<GitHubAppOptions> options,
    GitHubAppKey key,
    TimeProvider clock) : IGitHubInstallationTokens
{
    public const string HttpClientName = "github-app-token";
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public volatile string? Token;
        public DateTimeOffset ExpiresAt;
    }

    private readonly ConcurrentDictionary<long, Entry> _entries = new();

    public async Task<string> GetAsync(long installationId, CancellationToken ct)
    {
        var entry = _entries.GetOrAdd(installationId, _ => new Entry());
        await entry.Gate.WaitAsync(ct);
        try
        {
            if (entry.Token is { } cached && clock.GetUtcNow() < entry.ExpiresAt - RefreshMargin)
                return cached;
            var (token, expiresAt) = await MintAsync(installationId, ct);
            entry.ExpiresAt = expiresAt;
            entry.Token = token;
            return token;
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public void Invalidate(long installationId)
    {
        if (_entries.TryGetValue(installationId, out var entry))
            entry.Token = null;
    }

    private async Task<(string Token, DateTimeOffset ExpiresAt)> MintAsync(long installationId, CancellationToken ct)
    {
        var jwt = GitHubAppJwt.Create(options.Value.AppId, key.Get(), clock.GetUtcNow());
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.github.com/app/installations/{installationId}/access_tokens");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await httpFactory.CreateClient(HttpClientName).SendAsync(request, ct);
        GitHubHttp.EnsureSuccess(response);
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        if (string.IsNullOrEmpty(body?.Token) || body.ExpiresAt is null)
            throw new GitHubSourceException(GitHubSourceErrorKind.Unexpected, "GitHub returned no installation token.");
        return (body.Token, body.ExpiresAt.Value);
    }

    private sealed record TokenResponse(
        string? Token,
        [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt);
}
