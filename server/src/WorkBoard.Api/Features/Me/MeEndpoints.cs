using WorkBoard.Api.Features.Members;
using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Features.Me;

public sealed record TenantInfo(Guid Id, string Name);

public sealed record TenantSettingsDto(bool SprintsEnabled, bool SizeEnabled, bool PriorityEnabled);

public sealed record MeResponse(Member Member, TenantInfo Tenant, TenantSettingsDto Settings);

public static class MeEndpoints
{
    public static void MapMe(this RouteGroupBuilder api) =>
        api.MapGet("/me", async (ICurrentUser user, TenantDb db, CancellationToken ct) =>
        {
            var identity = user.Identity;
            var settings = await db.ReadAsync(
                s => s.QuerySingleOrDefaultAsync<SettingsRow>(
                    "select sprints_enabled, size_enabled, priority_enabled from tenant_settings"),
                ct);

            return new MeResponse(
                identity.Member,
                new TenantInfo(identity.TenantId, identity.TenantName),
                new TenantSettingsDto(
                    settings?.SprintsEnabled ?? false,
                    settings?.SizeEnabled ?? false,
                    settings?.PriorityEnabled ?? false));
        });

    private sealed class SettingsRow
    {
        public bool SprintsEnabled { get; set; }
        public bool SizeEnabled { get; set; }
        public bool PriorityEnabled { get; set; }
    }
}
