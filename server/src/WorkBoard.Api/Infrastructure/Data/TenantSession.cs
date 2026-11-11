using System.Data.Common;
using Dapper;

namespace WorkBoard.Api.Infrastructure.Data;

// Handed out only by TenantDb, so every query through it runs inside the tenant-scoped transaction.
public sealed class TenantSession
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly CancellationToken _ct;

    internal TenantSession(DbConnection connection, DbTransaction transaction, CancellationToken ct)
    {
        _connection = connection;
        _transaction = transaction;
        _ct = ct;
    }

    public async Task<List<T>> QueryAsync<T>(string sql, object? param = null) =>
        (await _connection.QueryAsync<T>(Command(sql, param))).AsList();

    public Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? param = null) =>
        _connection.QuerySingleOrDefaultAsync<T>(Command(sql, param));

    public Task<int> ExecuteAsync(string sql, object? param = null) =>
        _connection.ExecuteAsync(Command(sql, param));

    private CommandDefinition Command(string sql, object? param) =>
        new(sql, param, _transaction, cancellationToken: _ct);
}
