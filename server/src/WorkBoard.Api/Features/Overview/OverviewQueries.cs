using WorkBoard.Api.Features.Issues;
using WorkBoard.Api.Features.Members;
using WorkBoard.Api.Infrastructure.Data;

namespace WorkBoard.Api.Features.Overview;

public static class OverviewQueries
{
    public const int StaleDays = 7;
    public const int GroupLimit = 5;
    public const int ActionItemDays = 14;
    public const int ActivityDays = 3;
    public const int ActivityLimit = 12;
    public const string TimeZone = "Asia/Seoul";

    // Monday 00:00 in Asia/Seoul of the current week, as an instant.
    public const string WeekStartSql =
        "(date_trunc('week', now() at time zone 'Asia/Seoul') at time zone 'Asia/Seoul')";

    public static async Task<Overview> BuildAsync(TenantSession s)
    {
        var counts = await CountsAsync(s);
        var attention = await AttentionAsync(s);
        var people = await PeopleAsync(s);
        var (projects, sprint) = (await ProjectsAsync(s), await SprintAsync(s));
        var actionItems = await ActionItemsAsync(s);
        var activity = await ActivityAsync(s);
        return new Overview(DateTime.UtcNow, counts, attention, people, projects, sprint, null, actionItems, activity);
    }

    private static async Task<OverviewCounts> CountsAsync(TenantSession s)
    {
        var c = (await s.QuerySingleOrDefaultAsync<CountsRow>($"""
            select count(*) filter (where i.state = 'open')::int as open,
                   count(*) filter (where i.state = 'open' and st.status = 'in_progress')::int as in_progress,
                   count(*) filter (where i.state = 'open' and st.status = 'in_review')::int as in_review,
                   count(*) filter (where i.state = 'open'
                                      and i.github_updated_at < now() - make_interval(days => @staleDays))::int as stale,
                   count(*) filter (where i.state = 'closed' and i.closed_at >= {WeekStartSql})::int as done_this_week
              from issues i
              join issue_status st on st.issue_id = i.id
            """, new { staleDays = StaleDays }))!;
        return new OverviewCounts(c.Open, c.InProgress, c.InReview, c.Stale, c.DoneThisWeek);
    }

    // ---- attention ----

    private static async Task<Attention> AttentionAsync(TenantSession s)
    {
        var priorityEnabled = await s.QuerySingleOrDefaultAsync<bool?>(
            "select priority_enabled from tenant_settings") ?? false;

        var urgentRows = priorityEnabled
            ? await s.QueryAsync<IssueIdRow>("""
                select i.id as issue_id, st.status
                  from issues i
                  join issue_status st on st.issue_id = i.id
                  join board_fields bf on bf.issue_id = i.id
                 where bf.priority = 'urgent' and st.status <> 'done'
                 order by i.github_updated_at, i.id
                """)
            : [];

        var reviewRows = await s.QueryAsync<ReviewRow>("""
            select issue_id, pr_number, pr_title, author_login, waiting_since
              from (
                select distinct on (i.id)
                       i.id as issue_id, p.number as pr_number, p.title as pr_title,
                       am.github_login as author_login, p.github_updated_at as waiting_since
                  from pull_requests p
                  join issue_pull_requests ipr on ipr.pull_request_id = p.id
                  join issues i on i.id = ipr.issue_id
                  left join members am on am.github_user_id = p.author_github_id
                 where p.state = 'open' and p.review_requested and i.state = 'open'
                 order by i.id, p.github_updated_at, p.id
              ) w
             order by waiting_since, issue_id
            """);

        var staleRows = await s.QueryAsync<StaleRow>("""
            select i.id as issue_id,
                   floor(extract(epoch from now() - i.github_updated_at) / 86400)::int as idle_days
              from issues i
             where i.state = 'open' and i.github_updated_at < now() - make_interval(days => @staleDays)
             order by i.github_updated_at, i.id
            """, new { staleDays = StaleDays });

        var unassignedRows = await s.QueryAsync<IssueIdRow>("""
            select i.id as issue_id
              from issues i
             where i.state = 'open' and coalesce(i.issue_type, '') <> 'Epic'
               and not exists (select 1 from issue_assignees ia where ia.issue_id = i.id)
             order by i.github_updated_at, i.id
            """);

        // An issue shows only in the first group it belongs to.
        var seen = new HashSet<Guid>();
        var urgent = urgentRows.Where(r => seen.Add(r.IssueId)).ToList();
        var review = reviewRows.Where(r => seen.Add(r.IssueId)).ToList();
        var stale = staleRows.Where(r => seen.Add(r.IssueId)).ToList();
        var unassigned = unassignedRows.Where(r => seen.Add(r.IssueId)).ToList();

        var actionRows = await s.QueryAsync<ActionRow>("""
            select a.id, a.text, n.id as note_id, n.title as note_title, n.held_at,
                   m.id as member_id, m.github_user_id, m.github_login, m.display_name, m.avatar_url, m.role
              from meeting_action_items a
              join meeting_notes n on n.id = a.note_id
              left join members m on m.id = a.assignee_member_id
             where a.issue_id is null
             order by n.held_at, a.created_at, a.id
            """);

        var shown = urgent.Take(GroupLimit).Select(r => r.IssueId)
            .Concat(review.Take(GroupLimit).Select(r => r.IssueId))
            .Concat(stale.Take(GroupLimit).Select(r => r.IssueId))
            .Concat(unassigned.Take(GroupLimit).Select(r => r.IssueId))
            .ToArray();
        var refs = await LoadRefsAsync(s, shown);

        return new Attention(
            urgent.Take(GroupLimit).Select(r => new UrgentItem(refs[r.IssueId], r.Status == "todo")).ToList(),
            review.Take(GroupLimit)
                .Select(r => new ReviewWaitingItem(
                    refs[r.IssueId], new ReviewPullRequest(r.PrNumber, r.PrTitle, r.AuthorLogin, r.WaitingSince)))
                .ToList(),
            stale.Take(GroupLimit).Select(r => new StaleItem(refs[r.IssueId], r.IdleDays)).ToList(),
            unassigned.Take(GroupLimit).Select(r => refs[r.IssueId]).ToList(),
            actionRows.Take(GroupLimit)
                .Select(a => new ActionItemWithoutIssue(a.Id, a.Text, a.ToMember(), new NoteRef(a.NoteId, a.NoteTitle, a.HeldAt)))
                .ToList(),
            new AttentionTotals(urgent.Count, urgent.Count(r => r.Status == "todo"), review.Count, stale.Count, unassigned.Count, actionRows.Count));
    }

    private static async Task<Dictionary<Guid, OverviewIssueRef>> LoadRefsAsync(TenantSession s, Guid[] ids)
    {
        if (ids.Length == 0)
            return [];

        var rows = await s.QueryAsync<RefRow>("""
            select i.id, r.name as repository_name, i.number, i.title, st.status, i.github_updated_at as updated_at
              from issues i
              join repositories r on r.id = i.repository_id
              join issue_status st on st.issue_id = i.id
             where i.id = any(@ids)
            """, new { ids });
        var assignees = (await s.QueryAsync<AssigneeRow>("""
            select ia.issue_id, ia.github_user_id, m.github_login, m.display_name
              from issue_assignees ia
              left join members m on m.github_user_id = ia.github_user_id
             where ia.issue_id = any(@ids)
             order by ia.github_user_id
            """, new { ids })).ToLookup(a => a.IssueId);

        return rows.ToDictionary(
            r => r.Id,
            r => new OverviewIssueRef(
                r.Id, r.RepositoryName, r.Number, r.Title, r.Status, r.UpdatedAt,
                assignees[r.Id].Select(a => new AssigneeDto(a.GithubUserId, a.GithubLogin ?? "unknown", a.DisplayName)).ToList()));
    }

    // ---- people ----

    private static async Task<List<OverviewPerson>> PeopleAsync(TenantSession s)
    {
        var rows = await s.QueryAsync<PersonRow>($"""
            select {MemberRow.Columns},
                   count(i.id) filter (where i.state = 'open' and st.status = 'todo')::int as todo,
                   count(i.id) filter (where i.state = 'open' and st.status = 'in_progress')::int as in_progress,
                   count(i.id) filter (where i.state = 'open' and st.status = 'in_review')::int as in_review,
                   count(i.id) filter (where i.state = 'closed' and i.closed_at >= {WeekStartSql})::int as done_this_week,
                   greatest(max(i.github_updated_at),
                            (select max(c.github_created_at) from comments c
                              where c.author_github_id = m.github_user_id)) as last_activity_at
              from members m
              left join issue_assignees ia on ia.github_user_id = m.github_user_id
              left join issues i on i.id = ia.issue_id
              left join issue_status st on st.issue_id = i.id
             group by m.id
            """);

        var currentRows = await s.QueryAsync<CurrentRow>("""
            select distinct on (ia.github_user_id) ia.github_user_id, i.id as issue_id
              from issue_assignees ia
              join issues i on i.id = ia.issue_id
              join issue_status st on st.issue_id = i.id
             where i.state = 'open' and st.status in ('in_progress', 'in_review')
             order by ia.github_user_id, i.github_updated_at desc, i.id
            """);
        var refs = await LoadRefsAsync(s, currentRows.Select(c => c.IssueId).ToArray());
        var currentByUser = currentRows.ToDictionary(c => c.GithubUserId, c => refs[c.IssueId]);

        var team = rows.Select(r => r.InProgress + r.InReview).ToList();
        return rows
            .Select(r => new OverviewPerson(
                r.ToMember(), r.Todo, r.InProgress, r.InReview, r.DoneThisWeek,
                Workload.IsOverloaded(r.InProgress + r.InReview, team),
                currentByUser.GetValueOrDefault(r.GithubUserId), r.LastActivityAt))
            .OrderByDescending(p => p.Todo + p.InProgress + p.InReview)
            .ThenBy(p => p.Member.GithubLogin, StringComparer.Ordinal)
            .ToList();
    }

    // ---- projects and sprint ----

    private static async Task<List<OverviewProject>> ProjectsAsync(TenantSession s)
    {
        var projects = await s.QueryAsync<ProjectRow>("select id, name, color from projects order by name, id");
        var repos = (await s.QueryAsync<ProjectRepoRow>("""
            select pr.project_id, r.name
              from project_repositories pr
              join repositories r on r.id = pr.repository_id
             order by r.name
            """)).ToLookup(r => r.ProjectId, r => r.Name);
        var byStatus = (await s.QueryAsync<ProjectStatusRow>("""
            select pr.project_id,
                   count(*) filter (where st.status = 'todo')::int as todo,
                   count(*) filter (where st.status = 'in_progress')::int as in_progress,
                   count(*) filter (where st.status = 'in_review')::int as in_review,
                   count(*) filter (where st.status = 'done')::int as done
              from project_repositories pr
              join issues i on i.repository_id = pr.repository_id
              join issue_status st on st.issue_id = i.id
             group by pr.project_id
            """)).ToDictionary(r => r.ProjectId);
        var prCounts = (await s.QueryAsync<ProjectPrRow>("""
            select pr.project_id, count(*)::int as open_pull_requests
              from project_repositories pr
              join pull_requests p on p.repository_id = pr.repository_id
             where p.state = 'open'
             group by pr.project_id
            """)).ToDictionary(r => r.ProjectId, r => r.OpenPullRequests);
        var notes = (await s.QueryAsync<ProjectNoteRow>("""
            select distinct on (project_id) project_id, id, title, held_at
              from meeting_notes
             where project_id is not null
             order by project_id, held_at desc, id
            """)).ToDictionary(r => r.ProjectId);

        return projects
            .Select(p => new OverviewProject(
                new OverviewProjectRef(p.Id, p.Name, p.Color, repos[p.Id].ToList()),
                byStatus.TryGetValue(p.Id, out var c) ? new StatusCounts(c.Todo, c.InProgress, c.InReview, c.Done) : new StatusCounts(0, 0, 0, 0),
                prCounts.GetValueOrDefault(p.Id),
                notes.TryGetValue(p.Id, out var n) ? new NoteRef(n.Id, n.Title, n.HeldAt) : null))
            .ToList();
    }

    private static async Task<OverviewSprint?> SprintAsync(TenantSession s)
    {
        var enabled = await s.QuerySingleOrDefaultAsync<bool?>("select sprints_enabled from tenant_settings") ?? false;
        if (!enabled)
            return null;

        var sprint = await s.QuerySingleOrDefaultAsync<SprintRow>($"""
            select sp.id, sp.name, sp.start_date::text as start_date, sp.end_date::text as end_date,
                   greatest(sp.end_date - (now() at time zone '{TimeZone}')::date, 0)::int as days_left,
                   count(i.id) filter (where st.status = 'todo')::int as todo,
                   count(i.id) filter (where st.status = 'in_progress')::int as in_progress,
                   count(i.id) filter (where st.status = 'in_review')::int as in_review,
                   count(i.id) filter (where st.status = 'done')::int as done
              from (select * from sprints where state = 'active' order by start_date desc, id limit 1) sp
              left join board_fields bf on bf.sprint_id = sp.id
              left join issues i on i.id = bf.issue_id
              left join issue_status st on st.issue_id = i.id
             group by sp.id, sp.name, sp.start_date, sp.end_date
            """);
        return sprint is null
            ? null
            : new OverviewSprint(sprint.Id, sprint.Name, sprint.StartDate, sprint.EndDate, sprint.DaysLeft,
                new StatusCounts(sprint.Todo, sprint.InProgress, sprint.InReview, sprint.Done));
    }

    // ---- meeting action items ----

    private static async Task<List<OverviewActionItem>> ActionItemsAsync(TenantSession s)
    {
        var rows = await s.QueryAsync<ActionItemRow>("""
            select a.id, a.text, n.id as note_id, n.title as note_title, n.held_at,
                   m.id as member_id, m.github_user_id, m.github_login, m.display_name, m.avatar_url, m.role,
                   i.id as issue_id, i.number as issue_number, r.name as repository_name, st.status as issue_status
              from meeting_action_items a
              join meeting_notes n on n.id = a.note_id
              left join members m on m.id = a.assignee_member_id
              left join issues i on i.id = a.issue_id
              left join repositories r on r.id = i.repository_id
              left join issue_status st on st.issue_id = i.id
             where n.held_at >= now() - make_interval(days => @days)
               and (a.issue_id is null or st.status <> 'done')
             order by n.held_at desc, a.created_at, a.id
            """, new { days = ActionItemDays });

        return rows
            .Select(a => new OverviewActionItem(
                a.Id, a.Text, a.ToMember(),
                a.IssueId is { } issueId
                    ? new ActionItemIssue(issueId, a.RepositoryName ?? "", a.IssueNumber ?? 0, a.IssueStatus ?? "todo")
                    : null,
                new NoteRef(a.NoteId, a.NoteTitle, a.HeldAt)))
            .ToList();
    }

    // ---- activity ----

    private static async Task<List<ActivityItem>> ActivityAsync(TenantSession s)
    {
        var rows = await s.QueryAsync<ActivityRow>("""
            select * from (
              select 'comment' as kind, c.github_created_at as at, am.github_login as actor_login, am.display_name as actor_name,
                     i.id as issue_id, r.name as issue_repository, i.number as issue_number, i.title as issue_title,
                     null::text as pr_repository, null::int as pr_number, null::text as pr_title,
                     null::uuid as note_id, null::text as note_title, left(c.body, 80) as snippet
                from comments c
                join issues i on i.id = c.issue_id
                join repositories r on r.id = i.repository_id
                left join members am on am.github_user_id = c.author_github_id
               where c.github_created_at >= now() - make_interval(days => @days)
              union all
              select case when p.state = 'merged' then 'pr_merged'
                          when p.review_requested then 'pr_review_requested'
                          else 'pr_opened' end,
                     p.github_updated_at, am.github_login, am.display_name,
                     null, null, null, null,
                     r.name, p.number, p.title,
                     null, null, null
                from pull_requests p
                join repositories r on r.id = p.repository_id
                left join members am on am.github_user_id = p.author_github_id
               where p.state in ('open', 'merged') and p.github_updated_at >= now() - make_interval(days => @days)
              union all
              select 'issue_closed', i.closed_at, null, null,
                     i.id, r.name, i.number, i.title,
                     null, null, null,
                     null, null, null
                from issues i
                join repositories r on r.id = i.repository_id
               where i.state = 'closed' and i.closed_at >= now() - make_interval(days => @days)
              union all
              select 'meeting_note', n.held_at, am.github_login, am.display_name,
                     null, null, null, null,
                     null, null, null,
                     n.id, n.title, null
                from meeting_notes n
                left join members am on am.id = n.created_by
               where n.held_at >= now() - make_interval(days => @days) and n.held_at <= now()
            ) a
             order by at desc, kind
             limit @limit
            """, new { days = ActivityDays, limit = ActivityLimit });

        return rows
            .Select(a => new ActivityItem(
                a.Kind, a.At,
                a.ActorLogin is null && a.ActorName is null ? null : new ActivityActor(a.ActorLogin, a.ActorName),
                a.IssueId is { } iid ? new ActivityIssue(iid, a.IssueRepository ?? "", a.IssueNumber ?? 0, a.IssueTitle ?? "") : null,
                a.PrNumber is { } prn ? new ActivityPullRequest(a.PrRepository ?? "", prn, a.PrTitle ?? "") : null,
                a.NoteId is { } nid ? new ActivityNote(nid, a.NoteTitle ?? "") : null,
                a.Snippet))
            .ToList();
    }

    // ---- rows ----

    private static Member? ToMember(Guid? id, long? userId, string? login, string? name, string? avatar, string? role) =>
        id is { } memberId ? new Member(memberId, userId ?? 0, login ?? "", name, avatar, role ?? "member") : null;

    private sealed class CountsRow
    {
        public int Open { get; set; }
        public int InProgress { get; set; }
        public int InReview { get; set; }
        public int Stale { get; set; }
        public int DoneThisWeek { get; set; }
    }

    private sealed class IssueIdRow
    {
        public Guid IssueId { get; set; }
        public string Status { get; set; } = "";
    }

    private sealed class ReviewRow
    {
        public Guid IssueId { get; set; }
        public int PrNumber { get; set; }
        public string PrTitle { get; set; } = "";
        public string? AuthorLogin { get; set; }
        public DateTime WaitingSince { get; set; }
    }

    private sealed class StaleRow
    {
        public Guid IssueId { get; set; }
        public int IdleDays { get; set; }
    }

    private class MemberColumns
    {
        public Guid? MemberId { get; set; }
        public long? GithubUserId { get; set; }
        public string? GithubLogin { get; set; }
        public string? DisplayName { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Role { get; set; }

        public Member? ToMember() => OverviewQueries.ToMember(MemberId, GithubUserId, GithubLogin, DisplayName, AvatarUrl, Role);
    }

    private class ActionRow : MemberColumns
    {
        public Guid Id { get; set; }
        public string Text { get; set; } = "";
        public Guid NoteId { get; set; }
        public string NoteTitle { get; set; } = "";
        public DateTime HeldAt { get; set; }
    }

    private sealed class ActionItemRow : ActionRow
    {
        public Guid? IssueId { get; set; }
        public int? IssueNumber { get; set; }
        public string? RepositoryName { get; set; }
        public string? IssueStatus { get; set; }
    }

    private sealed class RefRow
    {
        public Guid Id { get; set; }
        public string RepositoryName { get; set; } = "";
        public int Number { get; set; }
        public string Title { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class AssigneeRow
    {
        public Guid IssueId { get; set; }
        public long GithubUserId { get; set; }
        public string? GithubLogin { get; set; }
        public string? DisplayName { get; set; }
    }

    private sealed class PersonRow
    {
        public Guid Id { get; set; }
        public long GithubUserId { get; set; }
        public string GithubLogin { get; set; } = "";
        public string? DisplayName { get; set; }
        public string? AvatarUrl { get; set; }
        public string Role { get; set; } = "member";
        public int Todo { get; set; }
        public int InProgress { get; set; }
        public int InReview { get; set; }
        public int DoneThisWeek { get; set; }
        public DateTime? LastActivityAt { get; set; }

        public Member ToMember() => new(Id, GithubUserId, GithubLogin, DisplayName, AvatarUrl, Role);
    }

    private sealed class CurrentRow
    {
        public long GithubUserId { get; set; }
        public Guid IssueId { get; set; }
    }

    private sealed class ProjectRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string? Color { get; set; }
    }

    private sealed class ProjectRepoRow
    {
        public Guid ProjectId { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class ProjectStatusRow
    {
        public Guid ProjectId { get; set; }
        public int Todo { get; set; }
        public int InProgress { get; set; }
        public int InReview { get; set; }
        public int Done { get; set; }
    }

    private sealed class ProjectPrRow
    {
        public Guid ProjectId { get; set; }
        public int OpenPullRequests { get; set; }
    }

    private sealed class ProjectNoteRow
    {
        public Guid ProjectId { get; set; }
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public DateTime HeldAt { get; set; }
    }

    private sealed class SprintRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";
        public int DaysLeft { get; set; }
        public int Todo { get; set; }
        public int InProgress { get; set; }
        public int InReview { get; set; }
        public int Done { get; set; }
    }

    private sealed class ActivityRow
    {
        public string Kind { get; set; } = "";
        public DateTime At { get; set; }
        public string? ActorLogin { get; set; }
        public string? ActorName { get; set; }
        public Guid? IssueId { get; set; }
        public string? IssueRepository { get; set; }
        public int? IssueNumber { get; set; }
        public string? IssueTitle { get; set; }
        public string? PrRepository { get; set; }
        public int? PrNumber { get; set; }
        public string? PrTitle { get; set; }
        public Guid? NoteId { get; set; }
        public string? NoteTitle { get; set; }
        public string? Snippet { get; set; }
    }
}
