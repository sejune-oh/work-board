using WorkBoard.Api.Features.Projects;
using WorkBoard.Api.Infrastructure.Data;

namespace WorkBoard.Api.Features.Issues;

public sealed record IssueFilter(
    Guid? ProjectId,
    string? Person,
    int DoneWithinDays,
    IReadOnlyList<string>? Statuses = null,
    bool Stale = false,
    Guid? SprintId = null,
    bool AllClosed = false,
    bool DoneThisWeek = false);

public static class IssueQueries
{
    public static async Task<List<BoardIssue>> ListAsync(TenantSession s, IssueFilter filter)
    {
        var issues = await FetchAsync(s, $$"""
            (i.state = 'open' or @allClosed or @doneThisWeek or i.closed_at >= now() - make_interval(days => @doneWithinDays))
            and (not @doneThisWeek or (st.status = 'done' and i.closed_at >= {{Overview.OverviewQueries.WeekStartSql}}))
            and (@statuses::text[] is null or st.status = any(@statuses::text[]))
            and (not @stale or (i.state = 'open'
                   and i.github_updated_at < now() - make_interval(days => @staleDays)))
            and (@sprintId::uuid is null or bf.sprint_id = @sprintId::uuid)
            and (@projectId::uuid is null or exists (
                   select 1 from project_repositories pr
                    where pr.repository_id = i.repository_id and pr.project_id = @projectId::uuid))
            and (@assignee::text is null or exists (
                   select 1 from issue_assignees ia
                     join members am on am.github_user_id = ia.github_user_id
                    where ia.issue_id = i.id and am.github_login = @assignee::text))
            """, new
        {
            filter.ProjectId,
            assignee = filter.Person,
            filter.DoneWithinDays,
            allClosed = filter.AllClosed,
            doneThisWeek = filter.DoneThisWeek,
            statuses = filter.Statuses?.ToArray(),
            stale = filter.Stale,
            staleDays = Overview.OverviewQueries.StaleDays,
            filter.SprintId,
        });
        return issues;
    }

    public static async Task<BoardIssue?> GetAsync(TenantSession s, Guid id) =>
        (await FetchAsync(s, "i.id = @id", new { id })).SingleOrDefault();

    private static async Task<List<BoardIssue>> FetchAsync(TenantSession s, string where, object param)
    {
        var rows = await s.QueryAsync<IssueRow>($"""
            select i.id, i.repository_id, r.name as repository_name, i.number, i.title, i.issue_type, i.state,
                   st.status, st.category,
                   (select count(*) from issue_pull_requests ipr
                      join pull_requests p on p.id = ipr.pull_request_id
                     where ipr.issue_id = i.id and p.state = 'open')::int as open_pull_request_count,
                   (select count(*) from comments c where c.issue_id = i.id)::int as comment_count,
                   i.parent_issue_id, bf.sprint_id, sp.name as sprint_name, bf.size, bf.priority,
                   i.github_updated_at as updated_at, i.closed_at
              from issues i
              join repositories r on r.id = i.repository_id
              join issue_status st on st.issue_id = i.id
              left join board_fields bf on bf.issue_id = i.id
              left join sprints sp on sp.id = bf.sprint_id
             where {where}
             order by i.github_updated_at desc, i.id
            """, param);
        if (rows.Count == 0)
            return [];

        var ids = rows.Select(r => r.Id).ToArray();
        var assignees = (await s.QueryAsync<AssigneeRow>("""
            select ia.issue_id, ia.github_user_id, m.github_login, m.display_name
              from issue_assignees ia
              left join members m on m.github_user_id = ia.github_user_id
             where ia.issue_id = any(@ids)
             order by ia.github_user_id
            """, new { ids })).ToLookup(a => a.IssueId);
        var projects = (await s.QueryAsync<IssueProjectRow>("""
            select i.id as issue_id, pr.project_id
              from issues i
              join project_repositories pr on pr.repository_id = i.repository_id
             where i.id = any(@ids)
             order by pr.project_id
            """, new { ids })).ToLookup(p => p.IssueId, p => p.ProjectId);

        return rows.Select(r => new BoardIssue(
            r.Id,
            new RepositoryRef(r.RepositoryId, r.RepositoryName),
            projects[r.Id].ToList(),
            r.Number,
            r.Title,
            r.IssueType,
            r.State,
            r.Status,
            r.Category,
            assignees[r.Id]
                .Select(a => new AssigneeDto(a.GithubUserId, a.GithubLogin ?? "unknown", a.DisplayName))
                .ToList(),
            r.OpenPullRequestCount,
            r.CommentCount,
            r.ParentIssueId,
            r.SprintId is { } sprintId ? new SprintRef(sprintId, r.SprintName ?? "") : null,
            r.Size,
            r.Priority,
            r.UpdatedAt,
            r.ClosedAt)).ToList();
    }

    private sealed class IssueRow
    {
        public Guid Id { get; set; }
        public Guid RepositoryId { get; set; }
        public string RepositoryName { get; set; } = "";
        public int Number { get; set; }
        public string Title { get; set; } = "";
        public string? IssueType { get; set; }
        public string State { get; set; } = "";
        public string Status { get; set; } = "";
        public string Category { get; set; } = "";
        public int OpenPullRequestCount { get; set; }
        public int CommentCount { get; set; }
        public Guid? ParentIssueId { get; set; }
        public Guid? SprintId { get; set; }
        public string? SprintName { get; set; }
        public string? Size { get; set; }
        public string? Priority { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
    }

    private sealed class AssigneeRow
    {
        public Guid IssueId { get; set; }
        public long GithubUserId { get; set; }
        public string? GithubLogin { get; set; }
        public string? DisplayName { get; set; }
    }

    private sealed class IssueProjectRow
    {
        public Guid IssueId { get; set; }
        public Guid ProjectId { get; set; }
    }
}
