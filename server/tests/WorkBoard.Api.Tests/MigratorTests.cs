using Dapper;
using Npgsql;
using WorkBoard.Api.Infrastructure.Data.Migrations;

namespace WorkBoard.Api.Tests;

// Each test migrates a throwaway database created next to the real one, so nothing here touches shared data.
public class MigratorTests
{
    private static readonly string AdminConnection =
        Environment.GetEnvironmentVariable("ConnectionStrings__Migrate")
        ?? "Host=localhost;Port=5434;Database=workboard;Username=postgres;Password=postgres";

    private sealed class ScratchDb : IAsyncDisposable
    {
        private readonly string _name = "wb_mig_" + Guid.NewGuid().ToString("N")[..10];
        public string ConnectionString { get; private set; } = "";

        public static async Task<ScratchDb> CreateAsync()
        {
            var db = new ScratchDb();
            await using var admin = new NpgsqlConnection(AdminConnection);
            await admin.ExecuteAsync($"create database {db._name}");
            db.ConnectionString = new NpgsqlConnectionStringBuilder(AdminConnection) { Database = db._name, Pooling = false }.ConnectionString;
            return db;
        }

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            return await conn.ExecuteScalarAsync<T>(sql) ?? default!;
        }

        public async Task ExecAsync(string sql)
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.ExecuteAsync(sql);
        }

        public async ValueTask DisposeAsync()
        {
            await using var admin = new NpgsqlConnection(AdminConnection);
            await admin.ExecuteAsync($"drop database if exists {_name} with (force)");
        }
    }

    private static Migrator Real(ScratchDb db) => new(db.ConnectionString, EmbeddedSql.Migrations());

    [DbFact]
    public async Task Empty_database_gets_every_migration_once_and_records_them()
    {
        await using var db = await ScratchDb.CreateAsync();
        var all = EmbeddedSql.Migrations().Select(m => m.Name).Order(StringComparer.Ordinal).ToList();

        var first = await Real(db).RunAsync();
        var second = await Real(db).RunAsync();

        Assert.Equal(all, first.Applied);
        Assert.False(first.BaselineAdopted);
        Assert.Empty(second.Applied);
        Assert.Equal(all.Count, await db.ScalarAsync<long>("select count(*) from schema_migrations"));
        Assert.True(await db.ScalarAsync<bool>("select to_regclass('public.issues') is not null"));
    }

    [DbFact]
    public async Task Seed_is_loaded_only_into_a_database_built_from_nothing()
    {
        await using var db = await ScratchDb.CreateAsync();
        var seed = EmbeddedSql.Seed()!;

        var first = await Real(db).RunAsync(seed);
        await db.ExecAsync("delete from tenants");
        var second = await Real(db).RunAsync(seed);

        Assert.True(first.Seeded);
        Assert.False(second.Seeded);
        Assert.Equal(0, await db.ScalarAsync<long>("select count(*) from tenants"));
    }

    [DbFact]
    public async Task Database_created_before_migrations_keeps_its_rows_and_gets_the_newer_migrations()
    {
        await using var db = await ScratchDb.CreateAsync();
        var migrations = EmbeddedSql.Migrations().OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
        await db.ExecAsync(migrations[0].Sql);
        var linkId = Guid.NewGuid();
        await db.ExecAsync($"""
            insert into tenants (id, name) values ('10000000-0000-0000-0000-0000000000aa', 'Legacy Co');
            insert into members (tenant_id, github_user_id, github_login) values ('10000000-0000-0000-0000-0000000000aa', 42, 'legacy-dev');
            insert into auth_links (auth_user_id, github_user_id) values ('{linkId}', 42);
            """);

        var result = await Real(db).RunAsync(EmbeddedSql.Seed());

        Assert.True(result.BaselineAdopted);
        Assert.False(result.Seeded);
        Assert.Equal(migrations.Skip(1).Select(m => m.Name), result.Applied.Skip(1));
        Assert.Equal(migrations.Count, result.Applied.Count);
        Assert.Equal(1, await db.ScalarAsync<long>("select count(*) from members where github_login = 'legacy-dev'"));
        Assert.Equal(1, await db.ScalarAsync<long>($"select count(*) from auth_links where auth_user_id = '{linkId}'"));
        Assert.Equal(1, await db.ScalarAsync<long>("select count(*) from tenants"));
    }

    [DbFact]
    public async Task Failing_migration_rolls_back_only_that_file()
    {
        await using var db = await ScratchDb.CreateAsync();
        var migrations = new[]
        {
            new SqlMigration("0001_ok", "create table t_ok (id int)"),
            new SqlMigration("0002_bad", "create table t_half (id int); select 1 / 0"),
            new SqlMigration("0003_after", "create table t_after (id int)"),
        };

        var ex = await Assert.ThrowsAsync<MigrationException>(() => new Migrator(db.ConnectionString, migrations).RunAsync());

        Assert.Contains("0002_bad", ex.Message);
        Assert.Equal(["0001_ok"], await QueryNames(db));
        Assert.True(await db.ScalarAsync<bool>("select to_regclass('public.t_ok') is not null"));
        Assert.False(await db.ScalarAsync<bool>("select to_regclass('public.t_half') is not null"));
        Assert.False(await db.ScalarAsync<bool>("select to_regclass('public.t_after') is not null"));
    }

    [DbFact]
    public async Task Edited_migration_that_was_already_applied_is_refused()
    {
        await using var db = await ScratchDb.CreateAsync();
        await new Migrator(db.ConnectionString, [new SqlMigration("0001_x", "create table t_x (id int)")]).RunAsync();

        var ex = await Assert.ThrowsAsync<MigrationException>(() =>
            new Migrator(db.ConnectionString, [new SqlMigration("0001_x", "create table t_x (id bigint)")]).RunAsync());

        Assert.Contains("0001_x", ex.Message);
    }

    private static async Task<string[]> QueryNames(ScratchDb db)
    {
        await using var conn = new NpgsqlConnection(db.ConnectionString);
        return (await conn.QueryAsync<string>("select name from schema_migrations order by name")).ToArray();
    }
}
