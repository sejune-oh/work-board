using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace WorkBoard.Api.Infrastructure.GitHub;

public sealed class GitHubAppClient(HttpClient http, IOptions<GitHubAppOptions> options, GitHubAppKey key) : IGitHubAppClient
{
    private const string ApiBase = "https://api.github.com";
    private const string TokenUrl = "https://github.com/login/oauth/access_token";
    private const int PageSize = 100;
    private const int MaxPages = 10;

    public async Task<string?> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        var o = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = o.ClientId!,
                ["client_secret"] = o.ClientSecret!,
                ["code"] = code,
            }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        return string.IsNullOrEmpty(body?.AccessToken) ? null : body.AccessToken;
    }

    public async Task<GitHubUser?> GetUserAsync(string userToken, CancellationToken ct)
    {
        using var response = await GetAsync($"{ApiBase}/user", userToken, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return null;
        response.EnsureSuccessStatusCode();
        var user = await response.Content.ReadFromJsonAsync<UserResponse>(ct);
        return user is null ? null : new GitHubUser(user.Id, user.Login);
    }

    public async Task<IReadOnlyList<long>?> ListUserInstallationIdsAsync(string userToken, CancellationToken ct)
    {
        var ids = new List<long>();
        for (var page = 1; page <= MaxPages; page++)
        {
            using var response = await GetAsync($"{ApiBase}/user/installations?per_page={PageSize}&page={page}", userToken, ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return null;
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<InstallationsResponse>(ct);
            var items = body?.Installations ?? [];
            ids.AddRange(items.Select(i => i.Id));
            if (items.Count < PageSize)
                break;
        }
        return ids;
    }

    public async Task<GitHubInstallation?> GetInstallationAsync(long installationId, CancellationToken ct)
    {
        var jwt = GitHubAppJwt.Create(options.Value.AppId, key.Get(), DateTimeOffset.UtcNow);
        using var response = await GetAsync($"{ApiBase}/app/installations/{installationId}", jwt, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<InstallationResponse>(ct);
        return body?.Account is null
            ? null
            : new GitHubInstallation(body.Account.Id, body.Account.Login, body.Account.Type, body.RepositorySelection);
    }

    private Task<HttpResponseMessage> GetAsync(string url, string bearer, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return http.SendAsync(request, ct);
    }

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string? AccessToken);

    private sealed record UserResponse(long Id, string Login);

    private sealed record InstallationsResponse(List<InstallationId>? Installations);

    private sealed record InstallationId(long Id);

    private sealed record InstallationResponse(
        AccountResponse? Account,
        [property: JsonPropertyName("repository_selection")] string? RepositorySelection);

    private sealed record AccountResponse(long Id, string Login, string Type);
}
