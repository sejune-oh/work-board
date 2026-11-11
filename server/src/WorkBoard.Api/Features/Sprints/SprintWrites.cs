using WorkBoard.Api.Features.Overview;
using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.Identity;
using WorkBoard.Api.Infrastructure.Writes;

namespace WorkBoard.Api.Features.Sprints;

// Every method runs on the caller's tenant transaction (inside Idempotency.RunAsync), so rows, issue events and
// the audit entry commit together. Non-2xx results are returned before anything is written.
public sealed class SprintWrites(ICurrentUser user, IAuditLog audit, IIssueEvents events)
{
    public const string SprintsDisabled = "sprints_disabled";
    public const string AlreadyUndone = "already_undone";
    public const string NameTaken = "name_taken";
    public const string InvalidDates = "invalid_dates";
    public const string SprintEnded = "sprint_ended";

    private sealed record NamedSprint(Guid Id, string Name, bool Ended);

    private sealed record IssueSprint(Guid Id, Guid? SprintId, string? SprintName);

    private sealed record SprintDates(Guid Id, string Name, string StartDate, string EndDate, string State, DateTime UpdatedAt);

    private sealed record CarryOverRow(Guid Id, Guid FromSprintId, Guid ToSprintId, DateTime? UndoneAt, DateTime CreatedAt);

    private static WriteResult Error(int status, string code) => new(status, new ErrorBody(code));

    private static WriteResult NotFound() => Error(StatusCodes.Status404NotFound, ApiErrors.NotFound);

    private static WriteResult Disabled() => Error(StatusCodes.Status409Conflict, SprintsDisabled);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");

    // Every writer of board_fields.sprint_id must take this lock first: assignment writes of a tenant run one at a
    // time, so the sprint an issue is read in is the one it is changed from.
    private Task LockAssignmentsAsync(TenantSession s) =>
        s.ExecuteAsync("select pg_advisory_xact_lock(hashtextextended(@scope, 0))",
            new { scope = $"sprint-assign:{user.Identity.TenantId:N}" });

    private Task LockNamesAsync(TenantSession s) =>
        s.ExecuteAsync("select pg_advisory_xact_lock(hashtextextended(@scope, 0))",
            new { scope = $"sprint-names:{user.Identity.TenantId:N}" });

    public async Task<WriteResult> CreateAsync(TenantSession s, string name, DateOnly start, DateOnly end)
    {
        if (!await SprintQueries.EnabledAsync(s))
            return Disabled();

        await LockNamesAsync(s);
        var id = await s.QuerySingleOrDefaultAsync<Guid?>($"""
            insert into sprints (tenant_id, name, start_date, end_date, state, created_by)
            values (@tenantId, @name, cast(@start as date), cast(@end as date),
                    case when cast(@start as date) > (now() at time zone '{OverviewQueries.TimeZone}')::date then 'planned' else 'active' end,
                    @memberId)
            on conflict (tenant_id, lower(name)) do nothing
            returning id
            """, new
        {
            tenantId = user.Identity.TenantId, name, start = Iso(start), end = Iso(end),
            memberId = user.Identity.Member.Id,
        });
        if (id is null)
            return Error(StatusCodes.Status409Conflict, NameTaken);

        var sprint = (await SprintQueries.GetAsync(s, id.Value))!;
        await audit.WriteAsync(s, "sprint.create", "sprint", sprint.Id, null, new { sprint.Name, sprint.StartDate, sprint.EndDate });
        return new WriteResult(StatusCodes.Status201Created, sprint);
    }

    public async Task<WriteResult> UpdateAsync(TenantSession s, Guid id, UpdateSprintRequest request)
    {
        if (!await SprintQueries.EnabledAsync(s))
            return Disabled();

        await LockNamesAsync(s);
        var current = await s.QuerySingleOrDefaultAsync<SprintDates>("""
            select id, name, start_date::text as start_date, end_date::text as end_date, state, updated_at
              from sprints where id = @id for update
            """, new { id });
        if (current is null)
            return NotFound();
        if (current.UpdatedAt != request.UpdatedAt!.Value.UtcDateTime)
            return Error(StatusCodes.Status409Conflict, ApiErrors.Conflict);

        var name = request.Name ?? current.Name;
        var start = request.StartDate is { } newStart ? Iso(newStart) : current.StartDate;
        var end = request.EndDate is { } newEnd ? Iso(newEnd) : current.EndDate;
        if (string.CompareOrdinal(end, start) < 0)
            return Error(StatusCodes.Status400BadRequest, InvalidDates);

        var taken = await s.QuerySingleOrDefaultAsync<bool>(
            "select exists (select 1 from sprints where lower(name) = lower(@name) and id <> @id)", new { name, id });
        if (taken)
            return Error(StatusCodes.Status409Conflict, NameTaken);

        await s.ExecuteAsync("""
            update sprints
               set name = @name, start_date = cast(@start as date), end_date = cast(@end as date), updated_at = now()
             where id = @id
            """, new { id, name, start, end });

        var sprint = (await SprintQueries.GetAsync(s, id))!;
        await audit.WriteAsync(s, "sprint.update", "sprint", id,
            new { current.Name, current.StartDate, current.EndDate },
            new { sprint.Name, sprint.StartDate, sprint.EndDate });
        return new WriteResult(StatusCodes.Status200OK, sprint);
    }

    public async Task<WriteResult> AddIssuesAsync(TenantSession s, Guid sprintId, Guid[] issueIds)
    {
        if (!await SprintQueries.EnabledAsync(s))
            return Disabled();
        await LockAssignmentsAsync(s);
        var target = await FindSprintAsync(s, sprintId);
        if (target is null)
            return NotFound();
        if (target.Ended)
            return Error(StatusCodes.Status409Conflict, SprintEnded);
        var rows = await LockIssuesAsync(s, issueIds);
        if (rows is null)
            return NotFound();

        var added = 0;
        var moved = new List<object>();
        foreach (var row in rows)
        {
            if (row.SprintId == sprintId)
                continue;
            await SetSprintAsync(s, row.Id, sprintId);
            if (row.SprintId is { } from)
            {
                await events.RecordAsync(s, new IssueEvent(row.Id, IssueEventKind.SprintRemoved, row.SprintName, null, from));
                moved.Add(new { issueId = row.Id, fromSprintId = from });
            }
            else
                added++;
            await events.RecordAsync(s, new IssueEvent(row.Id, IssueEventKind.SprintAdded, null, target.Name, sprintId));
        }

        var unchanged = rows.Count - added - moved.Count;
        await audit.WriteAsync(s, "sprint.issues_add", "sprint", sprintId, null,
            new { issueIds = rows.Select(r => r.Id), added, moved, unchanged });
        return new WriteResult(StatusCodes.Status200OK,
            new SprintIssuesResult((await SprintQueries.GetAsync(s, sprintId))!, added, moved.Count, 0, unchanged));
    }

    public async Task<WriteResult> RemoveIssuesAsync(TenantSession s, Guid sprintId, Guid[] issueIds)
    {
        if (!await SprintQueries.EnabledAsync(s))
            return Disabled();
        await LockAssignmentsAsync(s);
        var target = await FindSprintAsync(s, sprintId);
        if (target is null)
            return NotFound();
        var rows = await LockIssuesAsync(s, issueIds);
        if (rows is null)
            return NotFound();

        var removed = rows.Where(r => r.SprintId == sprintId).ToList();
        foreach (var row in removed)
        {
            await SetSprintAsync(s, row.Id, null);
            await events.RecordAsync(s, new IssueEvent(row.Id, IssueEventKind.SprintRemoved, target.Name, null, sprintId));
        }

        await audit.WriteAsync(s, "sprint.issues_remove", "sprint", sprintId, null,
            new { issueIds = rows.Select(r => r.Id), removed = removed.Select(r => r.Id) });
        return new WriteResult(StatusCodes.Status200OK,
            new SprintIssuesResult((await SprintQueries.GetAsync(s, sprintId))!, 0, 0, removed.Count, rows.Count - removed.Count));
    }

    public async Task<WriteResult> CarryOverAsync(TenantSession s, Guid fromId, Guid toId)
    {
        if (!await SprintQueries.EnabledAsync(s))
            return Disabled();
        await LockAssignmentsAsync(s);
        var from = await FindSprintAsync(s, fromId);
        var to = await FindSprintAsync(s, toId);
        if (from is null || to is null)
            return NotFound();
        if (to.Ended)
            return Error(StatusCodes.Status409Conflict, SprintEnded);

        var issueIds = (await s.QueryAsync<Guid>("""
            select i.id
              from board_fields bf
              join issues i on i.id = bf.issue_id
              join issue_status st on st.issue_id = i.id
             where bf.sprint_id = @fromId and st.status not in ('done', 'cancelled')
             order by i.id
            """, new { fromId })).ToArray();

        Guid? carryOverId = null;
        if (issueIds.Length > 0)
        {
            carryOverId = await s.QuerySingleOrDefaultAsync<Guid>("""
                insert into sprint_carry_overs (tenant_id, from_sprint_id, to_sprint_id, member_id)
                values (@tenantId, @fromId, @toId, @memberId) returning id
                """, new { tenantId = user.Identity.TenantId, fromId, toId, memberId = user.Identity.Member.Id });
            await s.ExecuteAsync("""
                insert into sprint_carry_over_issues (tenant_id, carry_over_id, issue_id)
                select @tenantId, @carryOverId, unnest(@issueIds)
                """, new { tenantId = user.Identity.TenantId, carryOverId, issueIds });
            await s.ExecuteAsync(
                "update board_fields set sprint_id = @toId, updated_at = now() where issue_id = any(@issueIds)",
                new { toId, issueIds });
            foreach (var issueId in issueIds)
                await events.RecordAsync(s, new IssueEvent(issueId, IssueEventKind.CarriedOver, from.Name, to.Name, toId));
            await audit.WriteAsync(s, "sprint.carry_over", "sprint", fromId, null,
                new { carryOverId, toSprintId = toId, issueIds });
        }

        return new WriteResult(StatusCodes.Status200OK, await ResultAsync(s, carryOverId, fromId, toId, issueIds));
    }

    public async Task<WriteResult> UndoCarryOverAsync(TenantSession s, Guid sprintId, Guid carryOverId)
    {
        if (!await SprintQueries.EnabledAsync(s))
            return Disabled();
        await LockAssignmentsAsync(s);
        var carry = await s.QuerySingleOrDefaultAsync<CarryOverRow>("""
            select id, from_sprint_id, to_sprint_id, undone_at, created_at from sprint_carry_overs
             where id = @carryOverId and from_sprint_id = @sprintId for update
            """, new { carryOverId, sprintId });
        if (carry is null)
            return NotFound();
        if (carry.UndoneAt is not null)
            return Error(StatusCodes.Status409Conflict, AlreadyUndone);

        var issueIds = (await s.QueryAsync<Guid>(
            "select issue_id from sprint_carry_over_issues where carry_over_id = @carryOverId order by issue_id",
            new { carryOverId })).ToArray();
        var rows = await LockIssuesAsync(s, issueIds);
        if (rows is null)
            return Error(StatusCodes.Status409Conflict, ApiErrors.Conflict);

        // Undo only when nothing touched the issues since: still in the target sprint, no later event
        // (this carry-over's own events share its transaction time), not finished.
        var blocking = (await s.QueryAsync<Guid>("""
            select i.id
              from issues i
              join issue_status st on st.issue_id = i.id
              left join board_fields bf on bf.issue_id = i.id
             where i.id = any(@issueIds)
               and (bf.sprint_id is distinct from @toId
                    or st.status in ('done', 'cancelled')
                    or exists (select 1 from issue_events e where e.issue_id = i.id and e.at > @createdAt))
             order by i.id
            """, new { issueIds, toId = carry.ToSprintId, createdAt = carry.CreatedAt })).ToArray();
        if (blocking.Length > 0)
            return new WriteResult(StatusCodes.Status409Conflict, new { error = ApiErrors.Conflict, issueIds = blocking });

        var from = (await FindSprintAsync(s, carry.FromSprintId))!;
        var to = (await FindSprintAsync(s, carry.ToSprintId))!;
        await s.ExecuteAsync(
            "update board_fields set sprint_id = @fromId, updated_at = now() where issue_id = any(@issueIds)",
            new { fromId = carry.FromSprintId, issueIds });
        foreach (var issueId in issueIds)
            await events.RecordAsync(s, new IssueEvent(issueId, IssueEventKind.CarriedOver, to.Name, from.Name, carry.FromSprintId));
        await s.ExecuteAsync(
            "update sprint_carry_overs set undone_at = now(), undone_by = @memberId where id = @carryOverId",
            new { carryOverId, memberId = user.Identity.Member.Id });
        await audit.WriteAsync(s, "sprint.carry_over_undo", "sprint", sprintId, null,
            new { carryOverId, toSprintId = carry.ToSprintId, issueIds });

        return new WriteResult(StatusCodes.Status200OK,
            await ResultAsync(s, carryOverId, carry.FromSprintId, carry.ToSprintId, issueIds));
    }

    private static async Task<CarryOverResult> ResultAsync(
        TenantSession s, Guid? carryOverId, Guid fromId, Guid toId, Guid[] issueIds) =>
        new(carryOverId, (await SprintQueries.GetAsync(s, fromId))!, (await SprintQueries.GetAsync(s, toId))!,
            issueIds.Length, issueIds);

    private static Task<NamedSprint?> FindSprintAsync(TenantSession s, Guid id) =>
        s.QuerySingleOrDefaultAsync<NamedSprint>($"""
            select id, name, (state = 'closed' or end_date < (now() at time zone '{OverviewQueries.TimeZone}')::date) as ended
              from sprints where id = @id
            """, new { id });

    // Null when any id is not an issue of this tenant (row security hides the others).
    private static async Task<List<IssueSprint>?> LockIssuesAsync(TenantSession s, Guid[] ids)
    {
        var rows = await s.QueryAsync<IssueSprint>("""
            select i.id, bf.sprint_id, sp.name as sprint_name
              from issues i
              left join board_fields bf on bf.issue_id = i.id
              left join sprints sp on sp.id = bf.sprint_id
             where i.id = any(@ids)
             order by i.id
            """, new { ids });
        return rows.Count == ids.Length ? rows : null;
    }

    private Task SetSprintAsync(TenantSession s, Guid issueId, Guid? sprintId) =>
        s.ExecuteAsync("""
            insert into board_fields (issue_id, tenant_id, sprint_id) values (@issueId, @tenantId, @sprintId)
            on conflict (issue_id) do update set sprint_id = excluded.sprint_id, updated_at = now()
            """, new { issueId, tenantId = user.Identity.TenantId, sprintId });
}
