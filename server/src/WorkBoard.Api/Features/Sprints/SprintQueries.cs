using WorkBoard.Api.Features.Overview;
using WorkBoard.Api.Infrastructure.Data;

namespace WorkBoard.Api.Features.Sprints;

public static class SprintQueries
{
    public const int PastLimit = 10;

    private static readonly string Today = $"(now() at time zone '{OverviewQueries.TimeZone}')::date";

    public static async Task<bool> EnabledAsync(TenantSession s) =>
        await s.QuerySingleOrDefaultAsync<bool?>("select sprints_enabled from tenant_settings") ?? false;

    public static async Task<SprintList> ListAsync(TenantSession s)
    {
        if (!await EnabledAsync(s))
            return new SprintList([], [], []);

        var all = await FetchAsync(s, null);
        return new SprintList(
            all.Where(x => x.State == "active").OrderByDescending(x => x.StartDate).ThenBy(x => x.Id).ToList(),
            all.Where(x => x.State == "planned").OrderBy(x => x.StartDate).ThenBy(x => x.Id).ToList(),
            all.Where(x => x.State == "closed").OrderByDescending(x => x.EndDate).ThenBy(x => x.Id).Take(PastLimit).ToList());
    }

    public static async Task<Sprint?> GetAsync(TenantSession s, Guid id) => (await FetchAsync(s, id)).SingleOrDefault();

    private static async Task<List<Sprint>> FetchAsync(TenantSession s, Guid? id)
    {
        var rows = await s.QueryAsync<SprintRow>($"""
            select sp.id, sp.name, sp.start_date::text as start_date, sp.end_date::text as end_date,
                   case when sp.state = 'closed' or sp.end_date < {Today} then 'closed'
                        when sp.start_date > {Today} then 'planned'
                        else 'active' end as state,
                   count(i.id) filter (where st.status <> 'cancelled')::int as issue_count,
                   count(i.id) filter (where st.status = 'done')::int as done_count,
                   count(i.id) filter (where st.status not in ('done', 'cancelled'))::int as unfinished_count,
                   sp.created_by, sp.created_at, sp.updated_at
              from sprints sp
              left join board_fields bf on bf.sprint_id = sp.id
              left join issues i on i.id = bf.issue_id
              left join issue_status st on st.issue_id = i.id
             where @id::uuid is null or sp.id = @id::uuid
             group by sp.id
             order by sp.start_date, sp.id
            """, new { id });
        return rows.Select(r => new Sprint(
            r.Id, r.Name, DateOnly.Parse(r.StartDate), DateOnly.Parse(r.EndDate), r.State,
            r.IssueCount, r.DoneCount, r.UnfinishedCount, r.CreatedBy, r.CreatedAt, r.UpdatedAt)).ToList();
    }

    private sealed class SprintRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";
        public string State { get; set; } = "";
        public int IssueCount { get; set; }
        public int DoneCount { get; set; }
        public int UnfinishedCount { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
