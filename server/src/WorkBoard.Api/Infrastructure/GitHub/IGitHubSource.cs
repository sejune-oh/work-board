namespace WorkBoard.Api.Infrastructure.GitHub;

public enum GitHubSourceErrorKind
{
    Unauthorized,
    Forbidden,
    NotFound,
    RateLimited,
    GraphQl,
    Unexpected,
}

public sealed class GitHubSourceException(GitHubSourceErrorKind kind, string message, DateTimeOffset? retryAt = null, Exception? inner = null)
    : Exception(message, inner)
{
    public GitHubSourceErrorKind Kind { get; } = kind;

    // Set for RateLimited when GitHub reports when the limit resets.
    public DateTimeOffset? RetryAt { get; } = retryAt;
}

public sealed record RateLimitInfo(int? Remaining, DateTimeOffset? ResetAt);

// Id is null for accounts GitHub does not expose a numeric id for (deleted users).
public sealed record SourceUser(long? Id, string Login, string? Name, string? AvatarUrl);

// Maps onto repositories (github_repo_id, name, full_name, html_url, archived).
public sealed record SourceRepository(long Id, string Name, string FullName, string? HtmlUrl, bool Archived);

// Maps onto labels. Id is null when the repository label list did not contain the name.
public sealed record SourceLabel(long? Id, string? NodeId, string Name, string? Color, string? Description);

// Maps onto milestones. State is "open" or "closed".
public sealed record SourceMilestone(
    long Id, int Number, string Title, string State, DateTimeOffset? DueOn, DateTimeOffset? ClosedAt, string? HtmlUrl);

// Maps onto issues, issue_labels and issue_assignees. State is "open" or "closed"; StateReason is one of
// completed, not_planned, reopened, duplicate. Parent* point at the parent issue of a sub-issue.
public sealed record SourceIssue(
    long DatabaseId,
    string NodeId,
    int Number,
    string Title,
    string? Body,
    string? IssueType,
    string State,
    string? StateReason,
    SourceUser? Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset UpdatedAt,
    string HtmlUrl,
    int? MilestoneNumber,
    IReadOnlyList<SourceLabel> Labels,
    IReadOnlyList<SourceUser> Assignees,
    long? ParentDatabaseId,
    int? ParentNumber);

// Feeds issue_pull_requests. RepositoryFullName can differ from the pull request's repository.
public sealed record ClosingIssueRef(long DatabaseId, int Number, string RepositoryFullName);

// Maps onto pull_requests. State is "open", "closed" or "merged".
public sealed record SourcePullRequest(
    long DatabaseId,
    string NodeId,
    int Number,
    string Title,
    string State,
    bool Draft,
    SourceUser? Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? MergedAt,
    DateTimeOffset? ClosedAt,
    string HtmlUrl,
    bool ReviewRequested,
    IReadOnlyList<SourceUser> RequestedReviewers,
    IReadOnlyList<ClosingIssueRef> ClosingIssues);

// Maps onto comments. IssueNumber is the number of the issue or pull request the comment belongs to.
public sealed record SourceComment(
    long Id,
    int IssueNumber,
    SourceUser? Author,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? HtmlUrl);

// Issues, PullRequests and Comments are ordered by UpdatedAt ascending and hold only items updated
// at or after the requested timestamp. Milestones and Labels are always the full repository lists.
public sealed record RepositorySnapshot(
    SourceRepository Repository,
    IReadOnlyList<SourceMilestone> Milestones,
    IReadOnlyList<SourceLabel> Labels,
    IReadOnlyList<SourceIssue> Issues,
    IReadOnlyList<SourcePullRequest> PullRequests,
    IReadOnlyList<SourceComment> Comments,
    RateLimitInfo? RateLimit);

public sealed record RepositoryList(IReadOnlyList<SourceRepository> Repositories, RateLimitInfo? RateLimit);

// Read-only access to GitHub through an installation token. Throws GitHubSourceException.
public interface IGitHubSource
{
    Task<RepositoryList> ListRepositoriesAsync(long installationId, CancellationToken ct);

    // since == null reads everything.
    Task<RepositorySnapshot> ReadRepositoryAsync(
        long installationId, string owner, string name, DateTimeOffset? since, CancellationToken ct);
}
