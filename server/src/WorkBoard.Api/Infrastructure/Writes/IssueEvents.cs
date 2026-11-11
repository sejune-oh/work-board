using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Infrastructure.Writes;

public static class IssueEventKind
{
    public const string SprintAdded = "sprint_added";
    public const string SprintRemoved = "sprint_removed";
    public const string StatusSet = "status_set";
    public const string StatusCleared = "status_cleared";
    public const string PrioritySet = "priority_set";
    public const string SizeSet = "size_set";
    public const string CarriedOver = "carried_over";
    public const string Closed = "closed";
    public const string Reopened = "reopened";
}

// For Closed and Reopened, FromValue and ToValue carry the GitHub state_reason where known.
public sealed record IssueEvent(Guid IssueId, string Kind, string? FromValue = null, string? ToValue = null, Guid? SprintId = null);

public interface IIssueEvents
{
    // Runs on the caller's session, so the event commits or rolls back with the change it describes.
    Task RecordAsync(TenantSession session, IssueEvent issueEvent);
}

public sealed class IssueEvents(ICurrentUser user) : IIssueEvents
{
    public Task RecordAsync(TenantSession session, IssueEvent issueEvent) =>
        session.ExecuteAsync("""
            insert into issue_events (tenant_id, issue_id, kind, from_value, to_value, sprint_id, member_id)
            values (@tenantId, @issueId, @kind, @fromValue, @toValue, @sprintId, @memberId)
            """, new
        {
            tenantId = user.Identity.TenantId,
            issueId = issueEvent.IssueId,
            kind = issueEvent.Kind,
            fromValue = issueEvent.FromValue,
            toValue = issueEvent.ToValue,
            sprintId = issueEvent.SprintId,
            memberId = user.Identity.Member.Id,
        });
}
