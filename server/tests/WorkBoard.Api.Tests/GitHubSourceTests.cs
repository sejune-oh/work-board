using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WorkBoard.Api.Infrastructure.GitHub;

namespace WorkBoard.Api.Tests;

public class GitHubSourceTests
{
    private sealed record Recorded(HttpMethod Method, Uri Uri, string Body, string? Auth);

    private sealed class FakeHandler(Func<Recorded, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Recorded> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var rec = new Recorded(request.Method, request.RequestUri!, body, request.Headers.Authorization?.ToString());
            lock (Requests)
                Requests.Add(rec);
            return respond(rec);
        }
    }

    private sealed class FakeTokens : IGitHubInstallationTokens
    {
        public int Invalidations { get; private set; }
        public int Mints { get; private set; }

        public Task<string> GetAsync(long installationId, CancellationToken ct) => Task.FromResult($"tok-{++Mints}");

        public void Invalidate(long installationId) => Invalidations++;
    }

    private sealed class FakeEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "GitHub", name));

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Status(HttpStatusCode code, string json = "{}") =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    // Routes the calls a ReadRepositoryAsync makes to the fixture files.
    private static HttpResponseMessage Route(Recorded r)
    {
        var path = r.Uri.AbsolutePath;
        if (path.EndsWith("/labels")) return Ok(Fixture("labels.json"));
        if (path.EndsWith("/milestones")) return Ok(Fixture("milestones.json"));
        if (path.EndsWith("/issues/comments")) return Ok(Fixture("comments.json"));
        if (path == "/installation/repositories") return Ok(Fixture("installation_repos.json"));
        if (path == "/graphql")
        {
            using var doc = JsonDocument.Parse(r.Body);
            var query = doc.RootElement.GetProperty("query").GetString()!;
            if (query.Contains("pullRequests("))
                return Ok(Fixture("prs.json"));
            var cursor = doc.RootElement.GetProperty("variables").GetProperty("cursor");
            return Ok(Fixture(cursor.ValueKind == JsonValueKind.Null ? "issues_page1.json" : "issues_page2.json"));
        }
        return Status(HttpStatusCode.NotFound);
    }

    private static (GitHubSource Source, FakeHandler Handler, FakeTokens Tokens) Create(Func<Recorded, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        var tokens = new FakeTokens();
        return (new GitHubSource(new HttpClient(handler), tokens), handler, tokens);
    }

    [Fact]
    public async Task Full_read_maps_issues_pull_requests_comments_milestones_and_labels()
    {
        var (source, handler, _) = Create(Route);

        var snap = await source.ReadRepositoryAsync(9, "octo-org", "hello", null, CancellationToken.None);

        Assert.Equal(new SourceRepository(1001, "hello", "octo-org/hello", "https://github.com/octo-org/hello", false), snap.Repository);

        Assert.Equal([1, 2], snap.Issues.Select(i => i.Number));
        var first = snap.Issues[0];
        Assert.Equal(5001, first.DatabaseId);
        Assert.Equal("I_kwA1", first.NodeId);
        Assert.Equal("closed", first.State);
        Assert.Equal("not_planned", first.StateReason);
        Assert.Equal("Bug", first.IssueType);
        Assert.Equal(new SourceUser(42, "octocat", "The Octocat", "https://avatars.example/octocat"), first.Author);
        Assert.Equal(3, first.MilestoneNumber);
        Assert.Equal(DateTimeOffset.Parse("2026-01-03T00:00:00Z"), first.UpdatedAt);
        Assert.Equal("https://github.com/octo-org/hello/issues/1", first.HtmlUrl);
        Assert.Equal(43, Assert.Single(first.Assignees).Id);
        Assert.Equal([901L, (long?)null], first.Labels.Select(l => l.Id));
        Assert.Equal("d73a4a", first.Labels[0].Color);

        var sub = snap.Issues[1];
        Assert.Equal("open", sub.State);
        Assert.Null(sub.StateReason);
        Assert.Null(sub.Author);
        Assert.Equal((5001L, 1), (sub.ParentDatabaseId, sub.ParentNumber));

        Assert.Equal([10, 11], snap.PullRequests.Select(p => p.Number));
        var pr = snap.PullRequests[1];
        Assert.Equal("open", pr.State);
        Assert.True(pr.Draft);
        Assert.True(pr.ReviewRequested);
        Assert.Equal(["hubot"], pr.RequestedReviewers.Select(u => u.Login));
        Assert.Equal(new ClosingIssueRef(5002, 2, "octo-org/hello"), Assert.Single(pr.ClosingIssues));
        var merged = snap.PullRequests[0];
        Assert.Equal("merged", merged.State);
        Assert.Equal(DateTimeOffset.Parse("2025-12-02T00:00:00Z"), merged.MergedAt);
        Assert.False(merged.ReviewRequested);

        var comment = Assert.Single(snap.Comments);
        Assert.Equal((601L, 1), (comment.Id, comment.IssueNumber));

        var milestone = Assert.Single(snap.Milestones);
        Assert.Equal((801L, 3, "open"), (milestone.Id, milestone.Number, milestone.State));
        Assert.Equal(901, Assert.Single(snap.Labels).Id);

        Assert.Equal(new RateLimitInfo(4970, DateTimeOffset.Parse("2026-01-01T01:00:00Z")), snap.RateLimit);
        Assert.All(handler.Requests, r => Assert.StartsWith("Bearer tok-", r.Auth));
    }

    [Fact]
    public async Task Issues_are_paged_with_the_end_cursor_and_since_is_sent()
    {
        var (source, handler, _) = Create(Route);
        var since = DateTimeOffset.Parse("2026-01-01T12:00:00+00:00");

        await source.ReadRepositoryAsync(9, "octo-org", "hello", since, CancellationToken.None);

        var issueCalls = handler.Requests
            .Where(r => r.Uri.AbsolutePath == "/graphql" && !r.Body.Contains("pullRequests("))
            .Select(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("variables"))
            .ToList();
        Assert.Equal(2, issueCalls.Count);
        Assert.Equal(JsonValueKind.Null, issueCalls[0].GetProperty("cursor").ValueKind);
        Assert.Equal("c1", issueCalls[1].GetProperty("cursor").GetString());
        Assert.Equal("2026-01-01T12:00:00Z", issueCalls[0].GetProperty("since").GetString());
        Assert.Contains("since=2026-01-01T12:00:00Z",
            Uri.UnescapeDataString(handler.Requests.Single(r => r.Uri.AbsolutePath.EndsWith("/issues/comments")).Uri.Query));
    }

    [Fact]
    public async Task Incremental_read_stops_at_pull_requests_older_than_since()
    {
        var (source, _, _) = Create(Route);

        var snap = await source.ReadRepositoryAsync(
            9, "octo-org", "hello", DateTimeOffset.Parse("2026-01-01T00:00:00Z"), CancellationToken.None);

        Assert.Equal([11], snap.PullRequests.Select(p => p.Number));
    }

    [Fact]
    public async Task Lists_installation_repositories_across_pages()
    {
        var page = 0;
        var (source, handler, _) = Create(r =>
        {
            page++;
            var repos = Enumerable.Range(0, page == 1 ? 100 : 1)
                .Select(i => $$"""{"id":{{page * 1000 + i}},"name":"r{{i}}","full_name":"octo-org/r{{i}}","html_url":null,"archived":{{(i == 0 ? "true" : "false")}}}""");
            return Ok($$"""{"total_count":101,"repositories":[{{string.Join(",", repos)}}]}""");
        });

        var list = await source.ListRepositoriesAsync(9, CancellationToken.None);

        Assert.Equal(101, list.Repositories.Count);
        Assert.True(list.Repositories[0].Archived);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("page=2", handler.Requests[1].Uri.Query);
    }

    [Fact]
    public async Task Rest_rate_limit_headers_are_surfaced()
    {
        var (source, _, _) = Create(_ =>
        {
            var response = Ok(Fixture("installation_repos.json"));
            response.Headers.Add("x-ratelimit-remaining", "123");
            response.Headers.Add("x-ratelimit-reset", "1767229200");
            return response;
        });

        var list = await source.ListRepositoriesAsync(9, CancellationToken.None);

        Assert.Equal(new RateLimitInfo(123, DateTimeOffset.FromUnixTimeSeconds(1767229200)), list.RateLimit);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, GitHubSourceErrorKind.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, GitHubSourceErrorKind.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, GitHubSourceErrorKind.Unexpected)]
    public async Task Http_failures_map_to_typed_exceptions(HttpStatusCode code, GitHubSourceErrorKind kind)
    {
        var (source, _, _) = Create(_ => Status(code, """{"message":"secret-ish detail"}"""));

        var e = await Assert.ThrowsAsync<GitHubSourceException>(() => source.ListRepositoriesAsync(9, CancellationToken.None));

        Assert.Equal(kind, e.Kind);
        Assert.DoesNotContain("secret-ish", e.Message);
        Assert.DoesNotContain("tok-", e.Message);
    }

    [Fact]
    public async Task Forbidden_with_exhausted_limit_is_rate_limited_with_reset_time()
    {
        var (source, _, _) = Create(_ =>
        {
            var response = Status(HttpStatusCode.Forbidden);
            response.Headers.Add("x-ratelimit-remaining", "0");
            response.Headers.Add("x-ratelimit-reset", "1767229200");
            return response;
        });

        var e = await Assert.ThrowsAsync<GitHubSourceException>(() => source.ListRepositoriesAsync(9, CancellationToken.None));

        Assert.Equal(GitHubSourceErrorKind.RateLimited, e.Kind);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1767229200), e.RetryAt);
    }

    [Fact]
    public async Task Unauthorized_invalidates_the_token_and_retries_once()
    {
        var calls = 0;
        var (source, _, tokens) = Create(_ => ++calls == 1 ? Status(HttpStatusCode.Unauthorized) : Ok(Fixture("installation_repos.json")));

        var list = await source.ListRepositoriesAsync(9, CancellationToken.None);

        Assert.Single(list.Repositories);
        Assert.Equal(1, tokens.Invalidations);
        Assert.Equal(2, tokens.Mints);
    }

    [Fact]
    public async Task Repeated_unauthorized_is_reported()
    {
        var (source, _, _) = Create(_ => Status(HttpStatusCode.Unauthorized));

        var e = await Assert.ThrowsAsync<GitHubSourceException>(() => source.ListRepositoriesAsync(9, CancellationToken.None));

        Assert.Equal(GitHubSourceErrorKind.Unauthorized, e.Kind);
    }

    [Theory]
    [InlineData("NOT_FOUND", GitHubSourceErrorKind.NotFound)]
    [InlineData("RATE_LIMITED", GitHubSourceErrorKind.RateLimited)]
    [InlineData("FORBIDDEN", GitHubSourceErrorKind.Forbidden)]
    [InlineData("SOMETHING_ELSE", GitHubSourceErrorKind.GraphQl)]
    public async Task Graphql_errors_map_to_typed_exceptions(string type, GitHubSourceErrorKind kind)
    {
        var (source, _, _) = Create(r => r.Uri.AbsolutePath == "/graphql"
            ? Ok($$"""{"data":null,"errors":[{"type":"{{type}}","message":"boom"}]}""")
            : Route(r));

        var e = await Assert.ThrowsAsync<GitHubSourceException>(
            () => source.ReadRepositoryAsync(9, "octo-org", "hello", null, CancellationToken.None));

        Assert.Equal(kind, e.Kind);
    }

    [Fact]
    public async Task Nested_graphql_errors_with_partial_data_are_ignored()
    {
        var (source, _, _) = Create(r =>
        {
            if (r.Uri.AbsolutePath != "/graphql" || !r.Body.Contains("pullRequests("))
                return Route(r);
            var json = Fixture("prs.json").TrimEnd()[..^1] +
                ""","errors":[{"type":"FORBIDDEN","message":"hidden","path":["repository","pullRequests","nodes",0,"closingIssuesReferences","nodes",0]}]}""";
            return Ok(json);
        });

        var snap = await source.ReadRepositoryAsync(9, "octo-org", "hello", null, CancellationToken.None);

        Assert.Equal([10, 11], snap.PullRequests.Select(p => p.Number));
    }

    [Fact]
    public async Task Repository_level_not_found_still_throws()
    {
        var (source, _, _) = Create(r => r.Uri.AbsolutePath == "/graphql"
            ? Ok("""{"data":{"repository":null},"errors":[{"type":"NOT_FOUND","message":"x","path":["repository"]}]}""")
            : Route(r));

        var e = await Assert.ThrowsAsync<GitHubSourceException>(
            () => source.ReadRepositoryAsync(9, "octo-org", "gone", null, CancellationToken.None));

        Assert.Equal(GitHubSourceErrorKind.NotFound, e.Kind);
    }

    [Fact]
    public async Task Rate_limited_graphql_error_falls_back_to_the_reset_header()
    {
        var (source, _, _) = Create(r =>
        {
            if (r.Uri.AbsolutePath != "/graphql")
                return Route(r);
            var response = Ok("""{"data":{"repository":{"databaseId":1,"nameWithOwner":"a/b","isArchived":false}},"errors":[{"type":"RATE_LIMITED","message":"x"}]}""");
            response.Headers.Add("x-ratelimit-reset", "1767229200");
            return response;
        });

        var e = await Assert.ThrowsAsync<GitHubSourceException>(
            () => source.ReadRepositoryAsync(9, "octo-org", "hello", null, CancellationToken.None));

        Assert.Equal(GitHubSourceErrorKind.RateLimited, e.Kind);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1767229200), e.RetryAt);
    }

    [Fact]
    public async Task Null_nested_nodes_are_tolerated()
    {
        var (source, _, _) = Create(r =>
        {
            if (r.Uri.AbsolutePath != "/graphql" || !r.Body.Contains("pullRequests("))
                return r.Uri.AbsolutePath == "/graphql" && r.Body.Contains("issues(")
                    ? Ok("""{"data":{"repository":{"databaseId":1,"nameWithOwner":"a/b","isArchived":false,"issues":{"pageInfo":{"hasNextPage":false},"nodes":[null,{"databaseId":1,"id":"i","number":1,"title":"t","state":"OPEN","createdAt":"2026-01-01T00:00:00Z","updatedAt":"2026-01-01T00:00:00Z","url":"u","issueType":null,"author":null,"milestone":null,"labels":{"nodes":[null]},"assignees":{"nodes":[null,{}]},"parent":null}]}}}}""")
                    : Route(r);
            return Ok("""{"data":{"repository":{"databaseId":1,"nameWithOwner":"a/b","isArchived":false,"pullRequests":{"pageInfo":{"hasNextPage":false},"nodes":[null,{"databaseId":2,"id":"p","number":2,"title":"t","state":"OPEN","isDraft":false,"createdAt":"2026-01-01T00:00:00Z","updatedAt":"2026-01-01T00:00:00Z","url":"u","author":null,"reviewRequests":{"nodes":[null,{"requestedReviewer":null}]},"closingIssuesReferences":{"nodes":[null]}}]}}}}""");
        });

        var snap = await source.ReadRepositoryAsync(9, "octo-org", "hello", null, CancellationToken.None);

        Assert.Equal(1, Assert.Single(snap.Issues).Number);
        Assert.Empty(snap.Issues[0].Assignees);
        Assert.Empty(snap.PullRequests[0].RequestedReviewers);
        Assert.Empty(snap.PullRequests[0].ClosingIssues);
    }

    [Fact]
    public async Task Missing_repository_in_graphql_data_is_not_found()
    {
        var (source, _, _) = Create(r => r.Uri.AbsolutePath == "/graphql" ? Ok("""{"data":{"repository":null}}""") : Route(r));

        var e = await Assert.ThrowsAsync<GitHubSourceException>(
            () => source.ReadRepositoryAsync(9, "octo-org", "gone", null, CancellationToken.None));

        Assert.Equal(GitHubSourceErrorKind.NotFound, e.Kind);
    }

    [Fact]
    public async Task Cancellation_is_respected()
    {
        var (source, handler, _) = Create(Route);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.ReadRepositoryAsync(9, "octo-org", "hello", null, cts.Token));

        Assert.Empty(handler.Requests);
    }

    // ---- installation tokens -----------------------------------------------------------------

    private sealed class TokenFixture : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
        public FakeClock Clock { get; } = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        public FakeHandler Handler { get; }
        public GitHubInstallationTokens Tokens { get; }
        public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(1);
        private int _minted;

        public TokenFixture(Func<Recorded, HttpResponseMessage>? respond = null)
        {
            var pem = Path.Combine(_dir, "key.pem");
            using (var rsa = RSA.Create(2048))
                File.WriteAllText(pem, rsa.ExportRSAPrivateKeyPem());
            var options = Options.Create(new GitHubAppOptions { AppId = 123, PrivateKeyPath = pem });
            Handler = new FakeHandler(respond ?? (_ =>
            {
                var n = Interlocked.Increment(ref _minted);
                return Ok($$"""{"token":"ghs_{{n}}","expires_at":"{{(Clock.Now + Lifetime):O}}"}""");
            }));
            Tokens = new GitHubInstallationTokens(
                new SingleClientFactory(new HttpClient(Handler)), options,
                new GitHubAppKey(options, new FakeEnv { ContentRootPath = _dir }), Clock);
        }

        public void Dispose() => Directory.Delete(_dir, true);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    [Fact]
    public async Task Token_is_minted_with_the_app_jwt_and_cached()
    {
        using var f = new TokenFixture();

        var a = await f.Tokens.GetAsync(7, CancellationToken.None);
        var b = await f.Tokens.GetAsync(7, CancellationToken.None);

        Assert.Equal("ghs_1", a);
        Assert.Equal(a, b);
        var request = Assert.Single(f.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/app/installations/7/access_tokens", request.Uri.AbsolutePath);
        Assert.StartsWith("Bearer eyJ", request.Auth);
    }

    [Fact]
    public async Task Token_is_cached_per_installation()
    {
        using var f = new TokenFixture();

        var a = await f.Tokens.GetAsync(1, CancellationToken.None);
        var b = await f.Tokens.GetAsync(2, CancellationToken.None);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task Token_is_renewed_within_five_minutes_of_expiry()
    {
        using var f = new TokenFixture();
        await f.Tokens.GetAsync(7, CancellationToken.None);

        f.Clock.Now += TimeSpan.FromMinutes(54);
        Assert.Equal("ghs_1", await f.Tokens.GetAsync(7, CancellationToken.None));

        f.Clock.Now += TimeSpan.FromMinutes(2);
        Assert.Equal("ghs_2", await f.Tokens.GetAsync(7, CancellationToken.None));
    }

    [Fact]
    public async Task Invalidate_forces_a_new_token()
    {
        using var f = new TokenFixture();
        await f.Tokens.GetAsync(7, CancellationToken.None);

        f.Tokens.Invalidate(7);

        Assert.Equal("ghs_2", await f.Tokens.GetAsync(7, CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_callers_share_one_mint()
    {
        using var f = new TokenFixture();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => f.Tokens.GetAsync(7, CancellationToken.None)));

        Assert.All(results, t => Assert.Equal("ghs_1", t));
        Assert.Single(f.Handler.Requests);
    }

    [Fact]
    public async Task Token_mint_failure_is_typed_and_does_not_leak_the_jwt()
    {
        using var f = new TokenFixture(_ => Status(HttpStatusCode.NotFound));

        var e = await Assert.ThrowsAsync<GitHubSourceException>(() => f.Tokens.GetAsync(7, CancellationToken.None));

        Assert.Equal(GitHubSourceErrorKind.NotFound, e.Kind);
        Assert.DoesNotContain("eyJ", e.Message);
    }

    [Fact]
    public async Task Token_network_failure_is_typed()
    {
        using var f = new TokenFixture(_ => throw new HttpRequestException("socket"));

        var e = await Assert.ThrowsAsync<GitHubSourceException>(() => f.Tokens.GetAsync(7, CancellationToken.None));

        Assert.Equal(GitHubSourceErrorKind.Unexpected, e.Kind);
    }

    [Fact]
    public async Task Token_malformed_json_is_typed()
    {
        using var f = new TokenFixture(_ => Ok("not json"));

        var e = await Assert.ThrowsAsync<GitHubSourceException>(() => f.Tokens.GetAsync(7, CancellationToken.None));

        Assert.Equal(GitHubSourceErrorKind.Unexpected, e.Kind);
    }

    [Fact]
    public async Task Token_timeout_is_typed_not_cancellation()
    {
        using var f = new TokenFixture(_ => throw new TaskCanceledException("timeout"));

        await Assert.ThrowsAsync<GitHubSourceException>(() => f.Tokens.GetAsync(7, CancellationToken.None));
    }

    [Fact]
    public async Task Token_caller_cancellation_propagates()
    {
        using var f = new TokenFixture();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Tokens.GetAsync(7, cts.Token));
    }
}
