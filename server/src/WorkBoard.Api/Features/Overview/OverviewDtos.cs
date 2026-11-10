using WorkBoard.Api.Features.Issues;
using WorkBoard.Api.Features.Members;

namespace WorkBoard.Api.Features.Overview;

public sealed record OverviewIssueRef(
    Guid Id, string RepositoryName, int Number, string Title, string Status, DateTime UpdatedAt,
    IReadOnlyList<AssigneeDto> Assignees);

public sealed record UrgentItem(OverviewIssueRef Issue, bool NotStarted);

public sealed record StaleItem(OverviewIssueRef Issue, int IdleDays);

public sealed record ReviewPullRequest(int Number, string Title, string? AuthorGithubLogin, DateTime WaitingSince);

public sealed record ReviewWaitingItem(OverviewIssueRef Issue, ReviewPullRequest PullRequest);

public sealed record NoteRef(Guid Id, string Title, DateTime HeldAt);

public sealed record ActionItemWithoutIssue(Guid Id, string Text, Member? Assignee, NoteRef Note);

public sealed record AttentionTotals(int Urgent, int UrgentNotStarted, int ReviewWaiting, int Stale, int Unassigned, int ActionItemsWithoutIssue);

public sealed record Attention(
    IReadOnlyList<UrgentItem> Urgent,
    IReadOnlyList<ReviewWaitingItem> ReviewWaiting,
    IReadOnlyList<StaleItem> Stale,
    IReadOnlyList<OverviewIssueRef> Unassigned,
    IReadOnlyList<ActionItemWithoutIssue> ActionItemsWithoutIssue,
    AttentionTotals Totals);

public sealed record OverviewCounts(int Open, int InProgress, int InReview, int Stale, int DoneThisWeek);

public sealed record OverviewPerson(
    Member Member, int Todo, int InProgress, int InReview, int DoneThisWeek, bool Overloaded,
    OverviewIssueRef? Current, DateTime? LastActivityAt);

public sealed record StatusCounts(int Todo, int InProgress, int InReview, int Done);

public sealed record OverviewProjectRef(Guid Id, string Name, string? Color, IReadOnlyList<string> Repositories);

public sealed record OverviewProject(
    OverviewProjectRef Project, StatusCounts ByStatus, int OpenPullRequests, NoteRef? LatestNote);

public sealed record OverviewSprint(
    Guid Id, string Name, string StartDate, string EndDate, int DaysLeft, StatusCounts ByStatus);

public sealed record ActionItemIssue(Guid Id, string RepositoryName, int Number, string Status);

public sealed record OverviewActionItem(Guid Id, string Text, Member? Assignee, ActionItemIssue? Issue, NoteRef Note);

public sealed record ActivityActor(string? GithubLogin, string? DisplayName);

public sealed record ActivityIssue(Guid Id, string RepositoryName, int Number, string Title);

public sealed record ActivityPullRequest(string RepositoryName, int Number, string Title);

public sealed record ActivityNote(Guid Id, string Title);

public sealed record ActivityItem(
    string Kind, DateTime At, ActivityActor? Actor, ActivityIssue? Issue, ActivityPullRequest? PullRequest,
    ActivityNote? Note, string? Snippet);

public sealed record Overview(
    DateTime GeneratedAt,
    OverviewCounts Counts,
    Attention Attention,
    IReadOnlyList<OverviewPerson> People,
    IReadOnlyList<OverviewProject> Projects,
    OverviewSprint? Sprint,
    object? Milestones,
    IReadOnlyList<OverviewActionItem> ActionItems,
    IReadOnlyList<ActivityItem> Activity);
