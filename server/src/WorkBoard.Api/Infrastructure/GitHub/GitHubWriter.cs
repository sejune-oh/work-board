using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkBoard.Api.Infrastructure.GitHub;

public sealed class GitHubWriter(HttpClient http, IGitHubInstallationTokens tokens) : IGitHubWriter
{
    private const string ApiBase = "https://api.github.com";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Footer(string login) => $"\n\n---\n_작성: @{login} (work-board)_";

    private static string WithFooter(string? body, string login) => (body ?? "").TrimEnd() + Footer(login);

    public async Task<SourceIssue> CreateIssueAsync(
        GitHubRepoRef repo, string actorLogin, string title, string? body,
        IReadOnlyList<string> labels, IReadOnlyList<string> assignees, CancellationToken ct) =>
        ToIssue(await SendAsync<RestIssue>(repo, HttpMethod.Post, IssuesUrl(repo),
            new { title, body = WithFooter(body, actorLogin), labels, assignees }, ct));

    public async Task<SourceIssue> SetAssigneesAsync(
        GitHubRepoRef repo, int number, IReadOnlyList<string> assignees, CancellationToken ct) =>
        ToIssue(await SendAsync<RestIssue>(repo, HttpMethod.Patch, IssueUrl(repo, number), new { assignees }, ct));

    public async Task<SourceComment> AddCommentAsync(
        GitHubRepoRef repo, string actorLogin, int number, string body, CancellationToken ct) =>
        ToComment(await SendAsync<RestComment>(repo, HttpMethod.Post, IssueUrl(repo, number) + "/comments",
            new { body = WithFooter(body, actorLogin) }, ct), number);

    public async Task<SourceIssue> CloseIssueAsync(GitHubRepoRef repo, int number, IssueCloseReason reason, CancellationToken ct) =>
        ToIssue(await SendAsync<RestIssue>(repo, HttpMethod.Patch, IssueUrl(repo, number),
            new { state = "closed", state_reason = reason == IssueCloseReason.NotPlanned ? "not_planned" : "completed" }, ct));

    public async Task<SourceIssue> ReopenIssueAsync(GitHubRepoRef repo, int number, CancellationToken ct) =>
        ToIssue(await SendAsync<RestIssue>(repo, HttpMethod.Patch, IssueUrl(repo, number),
            new { state = "open", state_reason = "reopened" }, ct));

    private static string IssuesUrl(GitHubRepoRef r) =>
        $"{ApiBase}/repos/{Uri.EscapeDataString(r.Owner)}/{Uri.EscapeDataString(r.Name)}/issues";

    private static string IssueUrl(GitHubRepoRef r, int number) => $"{IssuesUrl(r)}/{number}";

    // ---- transport ---------------------------------------------------------------------------

    private async Task<T> SendAsync<T>(GitHubRepoRef repo, HttpMethod method, string url, object payload, CancellationToken ct)
    {
        var content = JsonSerializer.Serialize(payload, Json);
        for (var attempt = 0; ; attempt++)
        {
            string token;
            try
            {
                token = await tokens.GetAsync(repo.InstallationId, ct);
            }
            catch (GitHubSourceException e)
            {
                throw new GitHubWriteException(GitHubWriteErrorKind.Unavailable, "GitHub token request failed.", inner: e);
            }

            using var request = new HttpRequestMessage(method, url)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, ct);
            }
            catch (HttpRequestException e)
            {
                throw new GitHubWriteException(GitHubWriteErrorKind.Unavailable, "GitHub request failed.", noResponse: true, inner: e);
            }
            catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
            {
                throw new GitHubWriteException(GitHubWriteErrorKind.Unavailable, "GitHub request timed out.", noResponse: true, inner: e);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    tokens.Invalidate(repo.InstallationId);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw await ToExceptionAsync(response, ct);
                try
                {
                    return await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw Malformed(null);
                }
                catch (JsonException e)
                {
                    throw Malformed(e);
                }
            }
        }
    }

    // The change was applied; only the answer is unreadable.
    private static GitHubWriteException Malformed(Exception? inner) =>
        new(GitHubWriteErrorKind.Unavailable, "GitHub returned an unreadable response.", sideEffectPossible: true, inner: inner);

    private static async Task<GitHubWriteException> ToExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var code = response.StatusCode;
        var status = $"GitHub responded with {(int)code}.";
        var limit = GitHubHttp.ReadRateLimit(response);
        var exhausted = limit?.Remaining == 0 || response.Headers.RetryAfter is not null;

        if (code == HttpStatusCode.TooManyRequests || (code == HttpStatusCode.Forbidden && exhausted))
            return new(GitHubWriteErrorKind.Unavailable, "GitHub rate limit reached.");
        if ((int)code >= 500)
            return new(GitHubWriteErrorKind.Unavailable, status, sideEffectPossible: true);

        return code switch
        {
            HttpStatusCode.Forbidden => new(GitHubWriteErrorKind.NotGranted, status),
            HttpStatusCode.Unauthorized => new(GitHubWriteErrorKind.Unavailable, status),
            HttpStatusCode.NotFound or HttpStatusCode.Gone => new(GitHubWriteErrorKind.NotFound, status),
            HttpStatusCode.UnprocessableEntity => new(
                await MentionsAssigneesAsync(response, ct) ? GitHubWriteErrorKind.AssigneeNotCollaborator : GitHubWriteErrorKind.InvalidRequest,
                status),
            _ => new(GitHubWriteErrorKind.InvalidRequest, status),
        };
    }

    private static bool Mentions(string? text) => text?.Contains("assignee", StringComparison.OrdinalIgnoreCase) == true;

    private static async Task<bool> MentionsAssigneesAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                foreach (var e in errors.EnumerateArray())
                {
                    if (e.ValueKind == JsonValueKind.String && Mentions(e.GetString()))
                        return true;
                    if (e.ValueKind == JsonValueKind.Object
                        && ((e.TryGetProperty("field", out var f) && Mentions(f.GetString()))
                            || (e.TryGetProperty("message", out var m) && Mentions(m.GetString()))))
                        return true;
                }
            return root.TryGetProperty("message", out var msg) && Mentions(msg.GetString());
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // ---- mapping -----------------------------------------------------------------------------

    private static SourceUser? ToUser(RestUser? u) => u is null ? null : new SourceUser(u.Id, u.Login, null, u.AvatarUrl);

    private static SourceIssue ToIssue(RestIssue i) =>
        new(i.Id, i.NodeId ?? "", i.Number, i.Title, i.Body, i.Type?.Name, i.State, i.StateReason, ToUser(i.User),
            i.CreatedAt, i.ClosedAt, i.UpdatedAt, i.HtmlUrl ?? "", i.Milestone?.Number,
            (i.Labels ?? []).Select(l => new SourceLabel(l.Id, l.NodeId, l.Name, l.Color, l.Description)).ToList(),
            (i.Assignees ?? []).Select(ToUser).OfType<SourceUser>().ToList(),
            null, null);

    private static SourceComment ToComment(RestComment c, int issueNumber) =>
        new(c.Id, issueNumber, ToUser(c.User), c.Body ?? "", c.CreatedAt, c.UpdatedAt, c.HtmlUrl);

    // ---- wire types --------------------------------------------------------------------------

    private sealed record RestUser(long Id, string Login, [property: JsonPropertyName("avatar_url")] string? AvatarUrl);

    private sealed record RestLabel(
        long Id, [property: JsonPropertyName("node_id")] string? NodeId, string Name, string? Color, string? Description);

    private sealed record RestName(string Name);

    private sealed record RestNumber(int Number);

    private sealed record RestIssue(
        long Id,
        [property: JsonPropertyName("node_id")] string? NodeId,
        int Number, string Title, string? Body, string State,
        [property: JsonPropertyName("state_reason")] string? StateReason,
        RestUser? User, RestName? Type, RestNumber? Milestone, List<RestLabel>? Labels, List<RestUser>? Assignees,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("closed_at")] DateTimeOffset? ClosedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);

    private sealed record RestComment(
        long Id, RestUser? User, string? Body,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);
}
