using System.Net;
using System.Text;
using System.Text.Json;
using WorkBoard.Api.Infrastructure.GitHub;
using WorkBoard.Api.Infrastructure.Writes;

namespace WorkBoard.Api.Tests;

public class GitHubWriterTests
{
    private sealed record Recorded(HttpMethod Method, Uri Uri, string Body, string? Auth, string? Accept, string? Version);

    private sealed class FakeHandler(Func<Recorded, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Recorded> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var rec = new Recorded(request.Method, request.RequestUri!, body, request.Headers.Authorization?.ToString(),
                request.Headers.Accept.ToString(), request.Headers.TryGetValues("X-GitHub-Api-Version", out var v) ? v.Single() : null);
            Requests.Add(rec);
            return respond(rec);
        }
    }

    private sealed class FakeTokens : IGitHubInstallationTokens
    {
        public int Invalidations { get; private set; }
        public int Mints { get; private set; }
        public bool Fail { get; set; }

        public Task<string> GetAsync(long installationId, CancellationToken ct) =>
            Fail ? throw new GitHubSourceException(GitHubSourceErrorKind.Forbidden, "no") : Task.FromResult($"tok-{++Mints}");

        public void Invalidate(long installationId) => Invalidations++;
    }

    private static readonly GitHubRepoRef Repo = new(42, "octo", "api");

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string IssueJson = """
        {"id":501,"node_id":"I_501","number":7,"title":"T","body":"b","state":"open","state_reason":null,
         "user":{"id":9,"login":"app[bot]","avatar_url":"https://a/9"},"type":{"name":"Bug"},"milestone":{"number":3},
         "labels":[{"id":11,"node_id":"L_11","name":"bug","color":"ff0000","description":"d"}],
         "assignees":[{"id":21,"login":"alice","avatar_url":"https://a/21"}],
         "created_at":"2026-11-01T09:00:00Z","closed_at":null,"updated_at":"2026-11-01T09:00:05Z","html_url":"https://github.com/octo/api/issues/7"}
        """;

    private static (GitHubWriter Writer, FakeHandler Handler, FakeTokens Tokens) Make(Func<Recorded, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        var tokens = new FakeTokens();
        return (new GitHubWriter(new HttpClient(handler), tokens), handler, tokens);
    }

    private static JsonElement Body(Recorded r) => JsonDocument.Parse(r.Body).RootElement;

    [Fact]
    public async Task Create_issue_posts_title_labels_assignees_and_footer_and_maps_the_response()
    {
        var (w, h, _) = Make(_ => Json(HttpStatusCode.Created, IssueJson));

        var issue = await w.CreateIssueAsync(Repo, "carol", "T", "hello  \n", ["bug"], ["alice"], default);

        var r = Assert.Single(h.Requests);
        Assert.Equal(HttpMethod.Post, r.Method);
        Assert.Equal("https://api.github.com/repos/octo/api/issues", r.Uri.ToString());
        Assert.Equal("Bearer tok-1", r.Auth);
        Assert.Equal("application/vnd.github+json", r.Accept);
        Assert.Equal("2022-11-28", r.Version);
        var b = Body(r);
        Assert.Equal("T", b.GetProperty("title").GetString());
        Assert.Equal("hello\n\n---\n_작성: @carol (work-board)_", b.GetProperty("body").GetString());
        Assert.Equal("bug", b.GetProperty("labels")[0].GetString());
        Assert.Equal("alice", b.GetProperty("assignees")[0].GetString());

        Assert.Equal(501, issue.DatabaseId);
        Assert.Equal(7, issue.Number);
        Assert.Equal("open", issue.State);
        Assert.Equal("Bug", issue.IssueType);
        Assert.Equal(3, issue.MilestoneNumber);
        Assert.Equal(11, issue.Labels.Single().Id);
        Assert.Equal("alice", issue.Assignees.Single().Login);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 9, 0, 5, TimeSpan.Zero), issue.UpdatedAt);
    }

    [Fact]
    public async Task Create_issue_without_body_still_gets_the_footer()
    {
        var (w, h, _) = Make(_ => Json(HttpStatusCode.Created, IssueJson));
        await w.CreateIssueAsync(Repo, "carol", "T", null, [], [], default);
        Assert.Equal("\n\n---\n_작성: @carol (work-board)_", Body(h.Requests[0]).GetProperty("body").GetString());
    }

    [Fact]
    public async Task Set_assignees_patches_the_issue()
    {
        var (w, h, _) = Make(_ => Json(HttpStatusCode.OK, IssueJson));
        await w.SetAssigneesAsync(Repo, 7, ["alice", "bob"], default);

        var r = Assert.Single(h.Requests);
        Assert.Equal(HttpMethod.Patch, r.Method);
        Assert.EndsWith("/repos/octo/api/issues/7", r.Uri.ToString());
        Assert.Equal(["alice", "bob"], Body(r).GetProperty("assignees").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public async Task Add_comment_posts_body_with_footer_and_maps_the_response()
    {
        var (w, h, _) = Make(_ => Json(HttpStatusCode.Created, """
            {"id":900,"user":{"id":9,"login":"app[bot]"},"body":"x","created_at":"2026-11-01T09:00:00Z",
             "updated_at":"2026-11-01T09:00:00Z","html_url":"https://github.com/octo/api/issues/7#issuecomment-900"}
            """));
        var c = await w.AddCommentAsync(Repo, "carol", 7, "hi", default);

        var r = Assert.Single(h.Requests);
        Assert.Equal(HttpMethod.Post, r.Method);
        Assert.EndsWith("/repos/octo/api/issues/7/comments", r.Uri.ToString());
        Assert.Equal("hi\n\n---\n_작성: @carol (work-board)_", Body(r).GetProperty("body").GetString());
        Assert.Equal(900, c.Id);
        Assert.Equal(7, c.IssueNumber);
    }

    [Theory]
    [InlineData(IssueCloseReason.Completed, "completed")]
    [InlineData(IssueCloseReason.NotPlanned, "not_planned")]
    public async Task Close_sends_state_and_reason(IssueCloseReason reason, string expected)
    {
        var (w, h, _) = Make(_ => Json(HttpStatusCode.OK, IssueJson));
        await w.CloseIssueAsync(Repo, 7, reason, default);

        var b = Body(Assert.Single(h.Requests));
        Assert.Equal("closed", b.GetProperty("state").GetString());
        Assert.Equal(expected, b.GetProperty("state_reason").GetString());
    }

    [Fact]
    public async Task Reopen_sends_open_with_reopened_reason()
    {
        var (w, h, _) = Make(_ => Json(HttpStatusCode.OK, IssueJson));
        await w.ReopenIssueAsync(Repo, 7, default);

        var b = Body(Assert.Single(h.Requests));
        Assert.Equal("open", b.GetProperty("state").GetString());
        Assert.Equal("reopened", b.GetProperty("state_reason").GetString());
    }

    [Fact]
    public async Task Unauthorized_drops_the_cached_token_and_retries_once()
    {
        var calls = 0;
        var (w, h, tokens) = Make(_ => ++calls == 1 ? Json(HttpStatusCode.Unauthorized, "{}") : Json(HttpStatusCode.OK, IssueJson));
        await w.ReopenIssueAsync(Repo, 7, default);

        Assert.Equal(2, h.Requests.Count);
        Assert.Equal(1, tokens.Invalidations);
        Assert.Equal("Bearer tok-2", h.Requests[1].Auth);
    }

    private static async Task<GitHubWriteException> FailAsync(Func<Recorded, HttpResponseMessage> respond)
    {
        var (w, _, _) = Make(respond);
        return await Assert.ThrowsAsync<GitHubWriteException>(() => w.ReopenIssueAsync(Repo, 7, default));
    }

    [Fact]
    public async Task Forbidden_without_rate_limit_is_not_granted()
    {
        var e = await FailAsync(_ => Json(HttpStatusCode.Forbidden, """{"message":"Resource not accessible by integration"}"""));
        Assert.Equal(GitHubWriteErrorKind.NotGranted, e.Kind);
        Assert.False(e.SideEffectPossible);
        var result = GitHubWriteErrors.ToResult(e);
        Assert.Equal(403, result.StatusCode);
        Assert.Contains("github_write_not_granted", JsonSerializer.Serialize(result.Body, JsonSerializerOptions.Web));
    }

    [Fact]
    public async Task Rate_limited_403_and_429_are_unavailable_and_not_sent()
    {
        var e = await FailAsync(_ =>
        {
            var r = Json(HttpStatusCode.Forbidden, "{}");
            r.Headers.Add("x-ratelimit-remaining", "0");
            return r;
        });
        Assert.Equal(GitHubWriteErrorKind.Unavailable, e.Kind);
        Assert.False(e.SideEffectPossible);

        e = await FailAsync(_ => Json(HttpStatusCode.TooManyRequests, "{}"));
        Assert.Equal(GitHubWriteErrorKind.Unavailable, e.Kind);
    }

    [Fact]
    public async Task Unprocessable_with_assignee_error_is_assignee_not_collaborator()
    {
        var e = await FailAsync(_ => Json(HttpStatusCode.UnprocessableEntity,
            """{"message":"Validation Failed","errors":[{"resource":"Issue","field":"assignees","code":"invalid","value":"bob"}]}"""));
        Assert.Equal(GitHubWriteErrorKind.AssigneeNotCollaborator, e.Kind);
        var result = GitHubWriteErrors.ToResult(e);
        Assert.Equal(422, result.StatusCode);
        Assert.Contains("assignee_not_collaborator", JsonSerializer.Serialize(result.Body, JsonSerializerOptions.Web));
    }

    [Fact]
    public async Task Other_unprocessable_is_invalid_request()
    {
        var e = await FailAsync(_ => Json(HttpStatusCode.UnprocessableEntity,
            """{"message":"Validation Failed","errors":[{"resource":"Issue","field":"title","code":"missing_field"}]}"""));
        Assert.Equal(GitHubWriteErrorKind.InvalidRequest, e.Kind);
        Assert.Equal(422, GitHubWriteErrors.ToResult(e).StatusCode);
    }

    [Fact]
    public async Task Not_found_maps_to_404()
    {
        var e = await FailAsync(_ => Json(HttpStatusCode.NotFound, "{}"));
        Assert.Equal(GitHubWriteErrorKind.NotFound, e.Kind);
        Assert.Equal(404, GitHubWriteErrors.ToResult(e).StatusCode);
    }

    [Fact]
    public async Task Server_error_is_502_and_may_have_had_a_side_effect()
    {
        var e = await FailAsync(_ => Json(HttpStatusCode.BadGateway, "{}"));
        Assert.Equal(GitHubWriteErrorKind.Unavailable, e.Kind);
        Assert.True(e.SideEffectPossible);
        Assert.False(e.NoResponse);
        Assert.Equal(502, GitHubWriteErrors.ToResult(e).StatusCode);
    }

    [Fact]
    public async Task Network_error_and_client_timeout_have_no_response_and_map_to_504()
    {
        var (w, _, _) = Make(_ => throw new HttpRequestException("reset by peer https://secret"));
        var net = await Assert.ThrowsAsync<GitHubWriteException>(() => w.ReopenIssueAsync(Repo, 7, default));
        Assert.True(net.NoResponse);
        Assert.True(net.SideEffectPossible);
        Assert.Equal(504, GitHubWriteErrors.ToResult(net).StatusCode);

        (w, _, _) = Make(_ => throw new TaskCanceledException("timeout"));
        var timeout = await Assert.ThrowsAsync<GitHubWriteException>(() => w.ReopenIssueAsync(Repo, 7, default));
        Assert.True(timeout.NoResponse);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_mapped()
    {
        using var cts = new CancellationTokenSource();
        var (w, _, _) = Make(_ =>
        {
            cts.Cancel();
            throw new TaskCanceledException("cancelled", null, cts.Token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => w.ReopenIssueAsync(Repo, 7, cts.Token));
    }

    [Fact]
    public async Task Token_failure_is_unavailable_and_nothing_is_sent()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, IssueJson));
        var w = new GitHubWriter(new HttpClient(handler), new FakeTokens { Fail = true });
        var e = await Assert.ThrowsAsync<GitHubWriteException>(() => w.ReopenIssueAsync(Repo, 7, default));

        Assert.Equal(GitHubWriteErrorKind.Unavailable, e.Kind);
        Assert.False(e.SideEffectPossible);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Error_messages_never_contain_tokens_or_response_data()
    {
        var e = await FailAsync(_ => Json(HttpStatusCode.UnprocessableEntity, """{"message":"secret-body tok-1"}"""));
        Assert.DoesNotContain("tok-1", e.Message);
        Assert.DoesNotContain("secret-body", e.Message);
    }

    [Fact]
    public async Task Unreadable_success_body_is_unavailable_with_possible_side_effect()
    {
        var e = await FailAsync(_ => Json(HttpStatusCode.OK, "not json"));
        Assert.Equal(GitHubWriteErrorKind.Unavailable, e.Kind);
        Assert.True(e.SideEffectPossible);
    }
}
