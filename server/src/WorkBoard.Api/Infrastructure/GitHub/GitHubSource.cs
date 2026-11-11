using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkBoard.Api.Infrastructure.GitHub;

public sealed class GitHubSource(HttpClient http, IGitHubInstallationTokens tokens) : IGitHubSource
{
    private const string ApiBase = "https://api.github.com";
    private const int PageSize = 100;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string Author = "author { login avatarUrl ... on User { databaseId name } ... on Bot { databaseId } }";

    private const string IssuesQuery = $$"""
        query($owner: String!, $name: String!, $cursor: String, $since: DateTime) {
          rateLimit { remaining resetAt }
          repository(owner: $owner, name: $name) {
            databaseId nameWithOwner url isArchived
            issues(first: 100, after: $cursor, orderBy: {field: UPDATED_AT, direction: ASC}, filterBy: {since: $since}) {
              pageInfo { hasNextPage endCursor }
              nodes {
                databaseId id number title body state stateReason createdAt closedAt updatedAt url
                issueType { name }
                {{Author}}
                milestone { number }
                labels(first: 50) { nodes { id name color description } }
                assignees(first: 20) { nodes { databaseId login name avatarUrl } }
                parent { databaseId number }
              }
            }
          }
        }
        """;

    private const string PullRequestsQuery = $$"""
        query($owner: String!, $name: String!, $cursor: String) {
          rateLimit { remaining resetAt }
          repository(owner: $owner, name: $name) {
            databaseId nameWithOwner url isArchived
            pullRequests(first: 100, after: $cursor, orderBy: {field: UPDATED_AT, direction: DESC}) {
              pageInfo { hasNextPage endCursor }
              nodes {
                databaseId id number title state isDraft createdAt closedAt updatedAt mergedAt url
                {{Author}}
                reviewRequests(first: 20) { nodes { requestedReviewer { ... on User { databaseId login name avatarUrl } } } }
                closingIssuesReferences(first: 20) { nodes { databaseId number repository { nameWithOwner } } }
              }
            }
          }
        }
        """;

    public async Task<RepositoryList> ListRepositoriesAsync(long installationId, CancellationToken ct)
    {
        var repos = new List<SourceRepository>();
        RateLimitInfo? limit = null;
        for (var page = 1; ; page++)
        {
            var (body, rl) = await GetRestAsync<InstallationRepositories>(
                installationId, $"{ApiBase}/installation/repositories?per_page={PageSize}&page={page}", ct);
            limit = rl ?? limit;
            var items = body.Repositories ?? [];
            repos.AddRange(items.Select(ToRepository));
            if (items.Count < PageSize)
                break;
        }
        return new RepositoryList(repos, limit);
    }

    public async Task<RepositorySnapshot> ReadRepositoryAsync(
        long installationId, string owner, string name, DateTimeOffset? since, CancellationToken ct)
    {
        var sinceText = since?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        RateLimitInfo? limit = null;
        SourceRepository? repository = null;

        var labelsRest = await GetRestPagesAsync<RestLabel>(
            installationId, $"{ApiBase}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/labels", ct);
        var milestonesRest = await GetRestPagesAsync<RestMilestone>(
            installationId,
            $"{ApiBase}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/milestones?state=all&sort=due_on",
            ct);
        limit = labelsRest.Limit ?? limit;
        limit = milestonesRest.Limit ?? limit;

        var labelIds = labelsRest.Items.ToDictionary(l => l.Name, l => l.Id, StringComparer.OrdinalIgnoreCase);
        var labels = labelsRest.Items.Select(l => new SourceLabel(l.Id, l.NodeId, l.Name, l.Color, l.Description)).ToList();
        var milestones = milestonesRest.Items.Select(ToMilestone).ToList();

        var issues = new List<SourceIssue>();
        string? cursor = null;
        while (true)
        {
            var (data, rl) = await PostGraphQlAsync<IssuesData>(installationId, IssuesQuery,
                new Dictionary<string, object?> { ["owner"] = owner, ["name"] = name, ["cursor"] = cursor, ["since"] = sinceText }, ct);
            limit = rl ?? limit;
            var repo = RequireRepository(data.Repository);
            repository ??= ToRepository(repo);
            var conn = repo.Issues ?? throw Unexpected();
            issues.AddRange((conn.Nodes ?? []).Where(n => n is not null).Select(n => ToIssue(n!, labelIds)));
            if (conn.PageInfo is not { HasNextPage: true, EndCursor: { } next })
                break;
            cursor = next;
        }

        var pullRequests = new List<SourcePullRequest>();
        cursor = null;
        var done = false;
        while (!done)
        {
            var (data, rl) = await PostGraphQlAsync<PullRequestsData>(installationId, PullRequestsQuery,
                new Dictionary<string, object?> { ["owner"] = owner, ["name"] = name, ["cursor"] = cursor }, ct);
            limit = rl ?? limit;
            var repo = RequireRepository(data.Repository);
            repository ??= ToRepository(repo);
            var conn = repo.PullRequests ?? throw Unexpected();
            foreach (var node in conn.Nodes ?? [])
            {
                if (node is null)
                    continue;
                if (since is { } s && node.UpdatedAt < s)
                {
                    done = true;
                    break;
                }
                pullRequests.Add(ToPullRequest(node));
            }
            if (conn.PageInfo is not { HasNextPage: true, EndCursor: { } next })
                break;
            cursor = next;
        }
        pullRequests.Reverse();

        var commentUrl = $"{ApiBase}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/issues/comments"
            + $"?sort=updated&direction=asc{(sinceText is null ? "" : "&since=" + sinceText)}";
        var commentsRest = await GetRestPagesAsync<RestComment>(installationId, commentUrl, ct);
        limit = commentsRest.Limit ?? limit;
        var comments = commentsRest.Items.Select(ToComment).ToList();

        return new RepositorySnapshot(repository!, milestones, labels, issues, pullRequests, comments, limit);
    }

    private static GitHubSourceException Unexpected() =>
        new(GitHubSourceErrorKind.Unexpected, "GitHub returned an unexpected response.");

    private static GqlRepository RequireRepository(GqlRepository? repo) =>
        repo ?? throw new GitHubSourceException(GitHubSourceErrorKind.NotFound, "Repository not found or not accessible.");

    // ---- transport ---------------------------------------------------------------------------

    private async Task<HttpResponseMessage> SendAsync(long installationId, Func<HttpRequestMessage> build, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await tokens.GetAsync(installationId, ct);
            using var request = build();
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
                throw new GitHubSourceException(GitHubSourceErrorKind.Unexpected, "GitHub request failed.", inner: e);
            }
            catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
            {
                throw new GitHubSourceException(GitHubSourceErrorKind.Unexpected, "GitHub request timed out.", inner: e);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                response.Dispose();
                tokens.Invalidate(installationId);
                continue;
            }
            try
            {
                GitHubHttp.EnsureSuccess(response);
            }
            catch
            {
                response.Dispose();
                throw;
            }
            return response;
        }
    }

    private async Task<(T Body, RateLimitInfo? Limit)> GetRestAsync<T>(long installationId, string url, CancellationToken ct)
    {
        using var response = await SendAsync(installationId, () => new HttpRequestMessage(HttpMethod.Get, url), ct);
        return (await ReadBodyAsync<T>(response, ct), GitHubHttp.ReadRateLimit(response));
    }

    private async Task<(List<T> Items, RateLimitInfo? Limit)> GetRestPagesAsync<T>(
        long installationId, string url, CancellationToken ct)
    {
        var items = new List<T>();
        RateLimitInfo? limit = null;
        var sep = url.Contains('?') ? '&' : '?';
        for (var page = 1; ; page++)
        {
            var (body, rl) = await GetRestAsync<List<T>>(installationId, $"{url}{sep}per_page={PageSize}&page={page}", ct);
            limit = rl ?? limit;
            items.AddRange(body);
            if (body.Count < PageSize)
                return (items, limit);
        }
    }

    private async Task<(T Data, RateLimitInfo? Limit)> PostGraphQlAsync<T>(
        long installationId, string query, Dictionary<string, object?> variables, CancellationToken ct)
        where T : GqlData
    {
        var payload = JsonSerializer.Serialize(new { query, variables }, Json);
        using var response = await SendAsync(installationId, () => new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/graphql")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        }, ct);
        var envelope = await ReadBodyAsync<GqlEnvelope<T>>(response, ct);

        if (envelope.Errors is { Count: > 0 } errors)
        {
            if (errors.Any(e => e.Type == "RATE_LIMITED"))
                throw new GitHubSourceException(GitHubSourceErrorKind.RateLimited, "GitHub rate limit reached.",
                    envelope.Data?.RateLimit?.ResetAt);
            if (errors.Any(e => e.Type == "NOT_FOUND"))
                throw new GitHubSourceException(GitHubSourceErrorKind.NotFound, "Repository not found or not accessible.");
            if (errors.Any(e => e.Type == "FORBIDDEN"))
                throw new GitHubSourceException(GitHubSourceErrorKind.Forbidden, "GitHub denied access.");
            throw new GitHubSourceException(GitHubSourceErrorKind.GraphQl,
                "GitHub GraphQL error: " + string.Join("; ", errors.Select(e => e.Message).Take(3)));
        }

        var data = envelope.Data ?? throw Unexpected();
        var gql = data.RateLimit is null ? null : new RateLimitInfo(data.RateLimit.Remaining, data.RateLimit.ResetAt);
        return (data, gql ?? GitHubHttp.ReadRateLimit(response));
    }

    private static async Task<T> ReadBodyAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw Unexpected();
        }
        catch (JsonException e)
        {
            throw new GitHubSourceException(GitHubSourceErrorKind.Unexpected, "GitHub returned malformed JSON.", inner: e);
        }
    }

    // ---- mapping -----------------------------------------------------------------------------

    private static SourceRepository ToRepository(GqlRepository r) =>
        new(r.DatabaseId, r.NameWithOwner[(r.NameWithOwner.IndexOf('/') + 1)..], r.NameWithOwner, r.Url, r.IsArchived);

    private static SourceRepository ToRepository(RestRepository r) =>
        new(r.Id, r.Name, r.FullName, r.HtmlUrl, r.Archived);

    private static SourceMilestone ToMilestone(RestMilestone m) =>
        new(m.Id, m.Number, m.Title, m.State, m.DueOn, m.ClosedAt, m.HtmlUrl);

    private static SourceUser? ToUser(GqlUser? u) =>
        u is null || u.Login is null ? null : new SourceUser(u.DatabaseId, u.Login, u.Name, u.AvatarUrl);

    private static SourceUser? ToUser(RestUser? u) =>
        u is null ? null : new SourceUser(u.Id, u.Login, null, u.AvatarUrl);

    private static SourceIssue ToIssue(GqlIssue n, Dictionary<string, long> labelIds) =>
        new(n.DatabaseId, n.Id, n.Number, n.Title, n.Body, n.IssueType?.Name,
            n.State.ToLowerInvariant(), n.StateReason?.ToLowerInvariant(), ToUser(n.Author),
            n.CreatedAt, n.ClosedAt, n.UpdatedAt, n.Url, n.Milestone?.Number,
            (n.Labels?.Nodes ?? []).Where(l => l is not null)
                .Select(l => new SourceLabel(labelIds.TryGetValue(l!.Name, out var id) ? id : null, l.Id, l.Name, l.Color, l.Description))
                .ToList(),
            (n.Assignees?.Nodes ?? []).Select(ToUser).OfType<SourceUser>().ToList(),
            n.Parent?.DatabaseId, n.Parent?.Number);

    private static SourcePullRequest ToPullRequest(GqlPullRequest n)
    {
        var reviewers = (n.ReviewRequests?.Nodes ?? [])
            .Select(r => ToUser(r?.RequestedReviewer)).OfType<SourceUser>().ToList();
        return new SourcePullRequest(n.DatabaseId, n.Id, n.Number, n.Title, n.State.ToLowerInvariant(), n.IsDraft,
            ToUser(n.Author), n.CreatedAt, n.UpdatedAt, n.MergedAt, n.ClosedAt, n.Url,
            (n.ReviewRequests?.Nodes?.Count ?? 0) > 0, reviewers,
            (n.ClosingIssuesReferences?.Nodes ?? []).Where(i => i is not null)
                .Select(i => new ClosingIssueRef(i!.DatabaseId, i.Number, i.Repository?.NameWithOwner ?? ""))
                .ToList());
    }

    private static SourceComment ToComment(RestComment c) =>
        new(c.Id, int.Parse(c.IssueUrl[(c.IssueUrl.LastIndexOf('/') + 1)..], CultureInfo.InvariantCulture),
            ToUser(c.User), c.Body ?? "", c.CreatedAt, c.UpdatedAt, c.HtmlUrl);

    // ---- wire types --------------------------------------------------------------------------

    private interface GqlData
    {
        GqlRateLimit? RateLimit { get; }
    }

    private sealed record GqlEnvelope<T>(T? Data, List<GqlError>? Errors);

    private sealed record GqlError(string? Type, string? Message);

    private sealed record GqlRateLimit(int Remaining, DateTimeOffset ResetAt);

    private sealed record PageInfo(bool HasNextPage, string? EndCursor);

    private sealed record Connection<T>(PageInfo? PageInfo, List<T?>? Nodes);

    private sealed record NodeList<T>(List<T?>? Nodes);

    private sealed record IssuesData(GqlRepository? Repository, GqlRateLimit? RateLimit) : GqlData;

    private sealed record PullRequestsData(GqlRepository? Repository, GqlRateLimit? RateLimit) : GqlData;

    private sealed record GqlRepository(
        long DatabaseId, string NameWithOwner, string? Url, bool IsArchived,
        Connection<GqlIssue>? Issues, Connection<GqlPullRequest>? PullRequests);

    private sealed record GqlUser(long? DatabaseId, string? Login, string? Name, string? AvatarUrl);

    private sealed record GqlName(string Name);

    private sealed record GqlNumber(int Number);

    private sealed record GqlLabel(string Id, string Name, string? Color, string? Description);

    private sealed record GqlParent(long DatabaseId, int Number);

    private sealed record GqlIssue(
        long DatabaseId, string Id, int Number, string Title, string? Body, string State, string? StateReason,
        DateTimeOffset CreatedAt, DateTimeOffset? ClosedAt, DateTimeOffset UpdatedAt, string Url,
        GqlName? IssueType, GqlUser? Author, GqlNumber? Milestone,
        NodeList<GqlLabel>? Labels, NodeList<GqlUser>? Assignees, GqlParent? Parent);

    private sealed record GqlReviewRequest(GqlUser? RequestedReviewer);

    private sealed record GqlClosingIssue(long DatabaseId, int Number, GqlRepoName? Repository);

    private sealed record GqlRepoName(string NameWithOwner);

    private sealed record GqlPullRequest(
        long DatabaseId, string Id, int Number, string Title, string State, bool IsDraft,
        DateTimeOffset CreatedAt, DateTimeOffset? ClosedAt, DateTimeOffset UpdatedAt, DateTimeOffset? MergedAt, string Url,
        GqlUser? Author, NodeList<GqlReviewRequest>? ReviewRequests, NodeList<GqlClosingIssue>? ClosingIssuesReferences);

    private sealed record InstallationRepositories(List<RestRepository>? Repositories);

    private sealed record RestRepository(
        long Id, string Name,
        [property: JsonPropertyName("full_name")] string FullName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        bool Archived);

    private sealed record RestLabel(
        long Id,
        [property: JsonPropertyName("node_id")] string? NodeId,
        string Name, string? Color, string? Description);

    private sealed record RestMilestone(
        long Id, int Number, string Title, string State,
        [property: JsonPropertyName("due_on")] DateTimeOffset? DueOn,
        [property: JsonPropertyName("closed_at")] DateTimeOffset? ClosedAt,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);

    private sealed record RestUser(
        long Id, string Login,
        [property: JsonPropertyName("avatar_url")] string? AvatarUrl);

    private sealed record RestComment(
        long Id,
        [property: JsonPropertyName("issue_url")] string IssueUrl,
        RestUser? User, string? Body,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);
}
