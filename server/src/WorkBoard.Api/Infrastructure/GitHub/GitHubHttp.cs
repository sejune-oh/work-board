using System.Globalization;
using System.Net;

namespace WorkBoard.Api.Infrastructure.GitHub;

internal static class GitHubHttp
{
    public static RateLimitInfo? ReadRateLimit(HttpResponseMessage response)
    {
        int? remaining = null;
        DateTimeOffset? reset = null;
        if (response.Headers.TryGetValues("x-ratelimit-remaining", out var r)
            && int.TryParse(r.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rem))
            remaining = rem;
        if (response.Headers.TryGetValues("x-ratelimit-reset", out var s)
            && long.TryParse(s.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
            reset = DateTimeOffset.FromUnixTimeSeconds(epoch);
        return remaining is null && reset is null ? null : new RateLimitInfo(remaining, reset);
    }

    // Messages carry the status only, never request or response data.
    public static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;
        var limit = ReadRateLimit(response);
        var code = response.StatusCode;
        var exhausted = limit?.Remaining == 0 || response.Headers.RetryAfter is not null;
        if (code == HttpStatusCode.TooManyRequests || (code == HttpStatusCode.Forbidden && exhausted))
        {
            var retryAt = limit?.ResetAt
                ?? (response.Headers.RetryAfter?.Delta is { } d ? DateTimeOffset.UtcNow + d : null);
            throw new GitHubSourceException(GitHubSourceErrorKind.RateLimited, "GitHub rate limit reached.", retryAt);
        }
        var kind = code switch
        {
            HttpStatusCode.Unauthorized => GitHubSourceErrorKind.Unauthorized,
            HttpStatusCode.Forbidden => GitHubSourceErrorKind.Forbidden,
            HttpStatusCode.NotFound => GitHubSourceErrorKind.NotFound,
            _ => GitHubSourceErrorKind.Unexpected,
        };
        throw new GitHubSourceException(kind, $"GitHub responded with {(int)code}.");
    }
}
