using WorkBoard.Api.Features.Projects;
using WorkBoard.Api.Infrastructure.Data;

namespace WorkBoard.Api.Features.Milestones;

public sealed record Milestone(
    Guid Id, RepositoryRef Repository, int Number, string Title, string State,
    DateTime? DueOn, string? HtmlUrl, int IssueCount, int DoneCount);

public static class MilestonesEndpoints
{
    public static void MapMilestones(this RouteGroupBuilder api) =>
        api.MapGet("/milestones", async (TenantDb db, CancellationToken ct) =>
        {
            var rows = await db.ReadAsync(s => s.QueryAsync<MilestoneRow>("""
                select m.id, m.repository_id, r.name as repository_name, m.number, m.title, m.state, m.due_on, m.html_url,
                       count(i.id) filter (where st.status <> 'cancelled')::int as issue_count,
                       count(i.id) filter (where st.status = 'done')::int as done_count
                  from milestones m
                  join repositories r on r.id = m.repository_id
                  left join issues i on i.milestone_id = m.id
                  left join issue_status st on st.issue_id = i.id
                 group by m.id, r.name
                 order by (m.state = 'open') desc, m.due_on nulls last, m.number, m.id
                """), ct);
            return rows.Select(m => new Milestone(
                m.Id, new RepositoryRef(m.RepositoryId, m.RepositoryName), m.Number, m.Title, m.State,
                m.DueOn, m.HtmlUrl, m.IssueCount, m.DoneCount)).ToList();
        });

    private sealed class MilestoneRow
    {
        public Guid Id { get; set; }
        public Guid RepositoryId { get; set; }
        public string RepositoryName { get; set; } = "";
        public int Number { get; set; }
        public string Title { get; set; } = "";
        public string State { get; set; } = "";
        public DateTime? DueOn { get; set; }
        public string? HtmlUrl { get; set; }
        public int IssueCount { get; set; }
        public int DoneCount { get; set; }
    }
}
