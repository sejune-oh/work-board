using WorkBoard.Api.Infrastructure.Data;

namespace WorkBoard.Api.Features.Issues;

public static class IssueDetailQueries
{
    public static async Task<IssueDetail?> GetAsync(TenantSession s, Guid id)
    {
        var issue = await IssueQueries.GetAsync(s, id);
        if (issue is null)
            return null;

        var body = await s.QuerySingleOrDefaultAsync<string?>("select body from issues where id = @id", new { id });

        var parent = issue.ParentIssueId is { } parentId
            ? await s.QuerySingleOrDefaultAsync<ParentRow>(
                "select id, number, title from issues where id = @parentId", new { parentId })
            : null;

        var children = await s.QueryAsync<ChildRow>("""
            select c.id, r.name as repository_name, c.number, c.title, st.status
              from issues c
              join repositories r on r.id = c.repository_id
              join issue_status st on st.issue_id = c.id
             where c.parent_issue_id = @id
             order by r.name, c.number
            """, new { id });

        var pullRequests = await s.QueryAsync<PullRequestRow>("""
            select p.id, p.number, p.title, p.state, p.draft, p.review_requested, p.html_url
              from issue_pull_requests ipr
              join pull_requests p on p.id = ipr.pull_request_id
             where ipr.issue_id = @id
             order by p.number
            """, new { id });

        var comments = await s.QueryAsync<CommentRow>("""
            select c.id, m.github_login, m.display_name, c.body, c.github_created_at as created_at
              from comments c
              left join members m on m.github_user_id = c.author_github_id
             where c.issue_id = @id
             order by c.github_created_at, c.id
            """, new { id });

        var meetingNotes = await s.QueryAsync<NoteRow>("""
            select n.id, n.title, n.held_at
              from meeting_notes n
             where exists (select 1 from meeting_action_items a where a.note_id = n.id and a.issue_id = @id)
             order by n.held_at desc
            """, new { id });

        return new IssueDetail(issue)
        {
            Body = body,
            Parent = parent is null ? null : new ParentRef(parent.Id, parent.Number, parent.Title),
            Children = children.Select(c => new ChildIssue(c.Id, c.RepositoryName, c.Number, c.Title, c.Status)).ToList(),
            PullRequests = pullRequests
                .Select(p => new PullRequestDto(p.Id, p.Number, p.Title, p.State, p.Draft, p.ReviewRequested, p.HtmlUrl))
                .ToList(),
            Comments = comments
                .Select(c => new CommentDto(c.Id, new CommentAuthor(c.GithubLogin, c.DisplayName), c.Body, c.CreatedAt))
                .ToList(),
            MeetingNotes = meetingNotes.Select(n => new LinkedMeetingNote(n.Id, n.Title, n.HeldAt)).ToList(),
        };
    }

    private sealed class ParentRow
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
        public string Title { get; set; } = "";
    }

    private sealed class ChildRow
    {
        public Guid Id { get; set; }
        public string RepositoryName { get; set; } = "";
        public int Number { get; set; }
        public string Title { get; set; } = "";
        public string Status { get; set; } = "";
    }

    private sealed class PullRequestRow
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
        public string Title { get; set; } = "";
        public string State { get; set; } = "";
        public bool Draft { get; set; }
        public bool ReviewRequested { get; set; }
        public string? HtmlUrl { get; set; }
    }

    private sealed class CommentRow
    {
        public Guid Id { get; set; }
        public string? GithubLogin { get; set; }
        public string? DisplayName { get; set; }
        public string Body { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    private sealed class NoteRow
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public DateTime HeldAt { get; set; }
    }
}
