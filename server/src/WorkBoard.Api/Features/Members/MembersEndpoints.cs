using WorkBoard.Api.Infrastructure.Data;

namespace WorkBoard.Api.Features.Members;

public static class MembersEndpoints
{
    public static void MapMembers(this RouteGroupBuilder api) =>
        api.MapGet("/members", async (TenantDb db, CancellationToken ct) =>
        {
            var rows = await db.ReadAsync(s => s.QueryAsync<MemberRow>(
                $"select {MemberRow.Columns} from members m order by m.github_login"), ct);
            return rows.Select(r => r.ToMember()).ToList();
        });
}
