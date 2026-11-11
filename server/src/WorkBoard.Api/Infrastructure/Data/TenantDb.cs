using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Infrastructure.Data;

// Single entry point for tenant data: opens the app-role connection, starts a transaction and sets
// app.tenant_id (transaction-local) before the caller's work runs.
public sealed class TenantDb(ICurrentUser user, IAppConnectionFactory connections)
{
    public const string SetTenantSql = "select set_config('app.tenant_id', @tenantId, true)";

    // Same transaction as ReadAsync: everything the work does commits together, or rolls back if it throws.
    public Task<T> WriteAsync<T>(Func<TenantSession, Task<T>> work, CancellationToken ct) => ReadAsync(work, ct);

    public async Task<T> ReadAsync<T>(Func<TenantSession, Task<T>> work, CancellationToken ct)
    {
        var tenantId = user.Identity.TenantId;

        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var session = new TenantSession(connection, transaction, ct);
        await session.ExecuteAsync(SetTenantSql, new { tenantId = tenantId.ToString() });

        var result = await work(session);
        await transaction.CommitAsync(ct);
        return result;
    }
}
