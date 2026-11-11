using System.Text.Json;
using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Infrastructure.Writes;

public interface IAuditLog
{
    // Runs on the caller's session, so the row commits or rolls back with the change it describes.
    Task WriteAsync(TenantSession session, string action, string targetType, Guid? targetId, object? before, object? after);
}

public sealed class AuditLog(ICurrentUser user) : IAuditLog
{
    public Task WriteAsync(TenantSession session, string action, string targetType, Guid? targetId, object? before, object? after) =>
        session.ExecuteAsync("""
            insert into audit_log (tenant_id, member_id, action, target_type, target_id, before, after)
            values (@tenantId, @memberId, @action, @targetType, @targetId, cast(@before as jsonb), cast(@after as jsonb))
            """, new
        {
            tenantId = user.Identity.TenantId,
            memberId = user.Identity.Member.Id,
            action,
            targetType,
            targetId,
            before = before is null ? null : JsonSerializer.Serialize(before, JsonSerializerOptions.Web),
            after = after is null ? null : JsonSerializer.Serialize(after, JsonSerializerOptions.Web),
        });
}
