using WorkBoard.Api.Features.Projects;

namespace WorkBoard.Api.Features.Issues;

public sealed record AssigneeDto(long GithubUserId, string GithubLogin, string? DisplayName);

public sealed record SprintRef(Guid Id, string Name);

public record BoardIssue(
    Guid Id,
    RepositoryRef Repository,
    IReadOnlyList<Guid> ProjectIds,
    int Number,
    string Title,
    string? IssueType,
    string State,
    string Status,
    string Category,
    IReadOnlyList<AssigneeDto> Assignees,
    int OpenPullRequestCount,
    int CommentCount,
    Guid? ParentIssueId,
    SprintRef? Sprint,
    string? Size,
    string? Priority,
    DateTime UpdatedAt,
    DateTime? ClosedAt);

public sealed record ParentRef(Guid Id, int Number, string Title);

public sealed record ChildIssue(Guid Id, string RepositoryName, int Number, string Title, string Status);

public sealed record PullRequestDto(Guid Id, int Number, string Title, string State, bool Draft, bool ReviewRequested);

public sealed record CommentAuthor(string? GithubLogin, string? DisplayName);

public sealed record CommentDto(Guid Id, CommentAuthor Author, string Body, DateTime CreatedAt);

public sealed record LinkedMeetingNote(Guid Id, string Title, DateTime HeldAt);

public sealed record IssueDetail : BoardIssue
{
    public IssueDetail(BoardIssue issue) : base(issue)
    {
    }

    public string? Body { get; init; }
    public ParentRef? Parent { get; init; }
    public IReadOnlyList<ChildIssue> Children { get; init; } = [];
    public IReadOnlyList<PullRequestDto> PullRequests { get; init; } = [];
    public IReadOnlyList<CommentDto> Comments { get; init; } = [];
    public IReadOnlyList<LinkedMeetingNote> MeetingNotes { get; init; } = [];
}
