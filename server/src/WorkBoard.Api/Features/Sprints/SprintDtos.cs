using WorkBoard.Api.Features.Issues;

namespace WorkBoard.Api.Features.Sprints;

// State is derived from the dates in Asia/Seoul: planned (not started), active, closed (ended or closed by hand).
public sealed record Sprint(
    Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, string State,
    int IssueCount, int DoneCount, int UnfinishedCount,
    Guid? CreatedBy, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record SprintList(IReadOnlyList<Sprint> Current, IReadOnlyList<Sprint> Upcoming, IReadOnlyList<Sprint> Past);

public sealed record SprintIssuesResult(Sprint Sprint, int Added, int Moved, int Removed, int Unchanged);

public sealed record CarryOverResult(
    Guid? CarryOverId, Sprint FromSprint, Sprint ToSprint, int MovedCount, IReadOnlyList<Guid> IssueIds);

public sealed record BacklogPage(IReadOnlyList<BoardIssue> Items, int Total, int Page, int PageSize);

public sealed record CreateSprintRequest(string? Name, DateOnly? StartDate, DateOnly? EndDate);

public sealed record UpdateSprintRequest(string? Name, DateOnly? StartDate, DateOnly? EndDate, DateTimeOffset? UpdatedAt);

public sealed record SprintIssuesRequest(Guid[]? IssueIds);

public sealed record CarryOverRequest(Guid? ToSprintId);
