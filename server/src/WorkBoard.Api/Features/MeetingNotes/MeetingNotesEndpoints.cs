using WorkBoard.Api.Features.Members;
using WorkBoard.Api.Infrastructure.Data;

namespace WorkBoard.Api.Features.MeetingNotes;

public static class MeetingNotesEndpoints
{
    public static void MapMeetingNotes(this RouteGroupBuilder api)
    {
        api.MapGet("/meeting-notes", async (Guid? projectId, TenantDb db, CancellationToken ct) =>
        {
            var rows = await db.ReadAsync(s => s.QueryAsync<SummaryRow>("""
                select n.id, n.title, n.held_at, n.project_id, p.name as project_name,
                       (select count(*) from meeting_action_items a where a.note_id = n.id)::int as action_item_count,
                       (select count(*) from meeting_action_items a
                         where a.note_id = n.id and a.issue_id is not null)::int as linked_action_item_count
                  from meeting_notes n
                  left join projects p on p.id = n.project_id
                 where @projectId::uuid is null or n.project_id = @projectId::uuid
                 order by n.held_at desc, n.id
                """, new { projectId }), ct);

            return rows
                .Select(r => new MeetingNoteSummary(
                    r.Id, r.Title, r.HeldAt, ToProject(r.ProjectId, r.ProjectName),
                    r.ActionItemCount, r.LinkedActionItemCount))
                .ToList();
        });

        api.MapGet("/meeting-notes/{id:guid}", async (Guid id, TenantDb db, CancellationToken ct) =>
        {
            var note = await db.ReadAsync(s => GetAsync(s, id), ct);
            return note is null ? Results.NotFound() : Results.Ok(note);
        });
    }

    private static async Task<MeetingNote?> GetAsync(TenantSession s, Guid id)
    {
        var note = await s.QuerySingleOrDefaultAsync<NoteRow>("""
            select n.id, n.title, n.held_at, n.body_md, n.project_id, p.name as project_name
              from meeting_notes n
              left join projects p on p.id = n.project_id
             where n.id = @id
            """, new { id });
        if (note is null)
            return null;

        var attendees = await s.QueryAsync<MemberRow>($"""
            select {MemberRow.Columns}
              from meeting_attendees ma
              join members m on m.id = ma.member_id
             where ma.note_id = @id
             order by m.github_login
            """, new { id });

        var items = await s.QueryAsync<ActionItemRow>("""
            select a.id, a.text,
                   m.id as member_id, m.github_user_id, m.github_login, m.display_name, m.avatar_url, m.role,
                   i.id as issue_id, i.number as issue_number, r.name as repository_name, st.status as issue_status
              from meeting_action_items a
              left join members m on m.id = a.assignee_member_id
              left join issues i on i.id = a.issue_id
              left join repositories r on r.id = i.repository_id
              left join issue_status st on st.issue_id = i.id
             where a.note_id = @id
             order by a.created_at, a.id
            """, new { id });

        return new MeetingNote(
            note.Id,
            note.Title,
            note.HeldAt,
            note.BodyMd,
            ToProject(note.ProjectId, note.ProjectName),
            attendees.Select(a => a.ToMember()).ToList(),
            items.Select(a => new ActionItemDto(
                a.Id,
                a.Text,
                a.MemberId is { } memberId
                    ? new Member(memberId, a.GithubUserId ?? 0, a.GithubLogin ?? "", a.DisplayName, a.AvatarUrl, a.Role ?? "member")
                    : null,
                a.IssueId is { } issueId
                    ? new ActionItemIssue(issueId, a.IssueNumber ?? 0, a.RepositoryName ?? "", a.IssueStatus ?? "todo")
                    : null)).ToList());
    }

    private static ProjectRef? ToProject(Guid? id, string? name) => id is { } projectId ? new ProjectRef(projectId, name ?? "") : null;

    private sealed class SummaryRow
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public DateTime HeldAt { get; set; }
        public Guid? ProjectId { get; set; }
        public string? ProjectName { get; set; }
        public int ActionItemCount { get; set; }
        public int LinkedActionItemCount { get; set; }
    }

    private sealed class NoteRow
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public DateTime HeldAt { get; set; }
        public string BodyMd { get; set; } = "";
        public Guid? ProjectId { get; set; }
        public string? ProjectName { get; set; }
    }

    private sealed class ActionItemRow
    {
        public Guid Id { get; set; }
        public string Text { get; set; } = "";
        public Guid? MemberId { get; set; }
        public long? GithubUserId { get; set; }
        public string? GithubLogin { get; set; }
        public string? DisplayName { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Role { get; set; }
        public Guid? IssueId { get; set; }
        public int? IssueNumber { get; set; }
        public string? RepositoryName { get; set; }
        public string? IssueStatus { get; set; }
    }
}
