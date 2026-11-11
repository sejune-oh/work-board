using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace WorkBoard.Api.Infrastructure.Data.Migrations;

public sealed record SqlMigration(string Name, string Sql)
{
    public string Checksum => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Sql)));
}

public sealed record MigrationResult(IReadOnlyList<string> Applied, bool BaselineAdopted, bool Seeded = false);

public sealed class MigrationException(string message, Exception? inner = null) : Exception(message, inner);

// Applies numbered SQL files in name order, each once and in its own transaction, and records them in
// schema_migrations. A database created before migrations existed (it has the 0001 tables but no history)
// adopts the first migration as its baseline instead of running it.
public sealed class Migrator(string connectionString, IReadOnlyList<SqlMigration> migrations)
{
    private const long LockKey = 7_446_901_001;

    public async Task<MigrationResult> RunAsync(string? seedSql = null, CancellationToken ct = default)
    {
        var ordered = migrations.OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        // Concurrent API hosts and the migrate command wait for each other here.
        await ExecAsync(conn, null, $"select pg_advisory_lock({LockKey})", ct);
        try
        {
            await ExecAsync(conn, null, """
                create table if not exists schema_migrations (
                  name       text primary key,
                  checksum   text not null,
                  applied_at timestamptz not null default now()
                )
                """, ct);

            var applied = new Dictionary<string, string>();
            await using (var cmd = new NpgsqlCommand("select name, checksum from schema_migrations", conn))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                    applied[reader.GetString(0)] = reader.GetString(1);

            foreach (var m in ordered.Where(m => applied.TryGetValue(m.Name, out var sum) && sum != m.Checksum))
                throw new MigrationException($"Migration {m.Name} was changed after it was applied. Add a new migration instead of editing it.");

            var newlyApplied = new List<string>();
            var adopted = false;
            foreach (var m in ordered.Where(m => !applied.ContainsKey(m.Name)))
            {
                var isBaseline = m == ordered[0] && applied.Count == 0 && await HasLegacySchemaAsync(conn, ct);
                try
                {
                    await using var tx = await conn.BeginTransactionAsync(ct);
                    if (!isBaseline)
                        await ExecAsync(conn, tx, m.Sql, ct);
                    await using var record = new NpgsqlCommand(
                        "insert into schema_migrations (name, checksum) values (@n, @c)", conn, tx);
                    record.Parameters.AddWithValue("n", m.Name);
                    record.Parameters.AddWithValue("c", m.Checksum);
                    await record.ExecuteNonQueryAsync(ct);
                    await tx.CommitAsync(ct);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    throw new MigrationException($"Migration {m.Name} failed and was rolled back: {e.Message}", e);
                }
                adopted |= isBaseline;
                newlyApplied.Add(m.Name);
            }

            // Sample data goes only into a database this run built from nothing, still under the lock.
            var seeded = false;
            if (seedSql is not null && newlyApplied.Count > 0 && !adopted && applied.Count == 0)
            {
                await using var tx = await conn.BeginTransactionAsync(ct);
                await ExecAsync(conn, tx, seedSql, ct);
                await tx.CommitAsync(ct);
                seeded = true;
            }
            return new MigrationResult(newlyApplied, adopted, seeded);
        }
        finally
        {
            await ExecAsync(conn, null, $"select pg_advisory_unlock({LockKey})", CancellationToken.None);
        }
    }

    private static async Task<bool> HasLegacySchemaAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "select to_regclass('public.tenants') is not null and to_regclass('public.auth_links') is not null", conn);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
