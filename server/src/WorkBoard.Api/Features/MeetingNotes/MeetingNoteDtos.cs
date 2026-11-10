using WorkBoard.Api.Features.Members;

namespace WorkBoard.Api.Features.MeetingNotes;

public sealed record ProjectRef(Guid Id, string Name);

public sealed record MeetingNoteSummary(
    Guid Id,
    string Title,
    DateTime HeldAt,
    ProjectRef? Project,
    int ActionItemCount,
    int LinkedActionItemCount);

public sealed record ActionItemIssue(Guid Id, int Number, string RepositoryName, string Status);

public sealed record ActionItemDto(Guid Id, string Text, Member? Assignee, ActionItemIssue? Issue);

public sealed record MeetingNote(
    Guid Id,
    string Title,
    DateTime HeldAt,
    string BodyMd,
    ProjectRef? Project,
    IReadOnlyList<Member> Attendees,
    IReadOnlyList<ActionItemDto> ActionItems);
