using Npgsql;

namespace WorkBoard.Api.Infrastructure.Data.Migrations;

public static class MigrationRunner
{
    // Applies pending migrations; on a database that had no schema at all, also loads the sample data.
    public static async Task<MigrationResult> RunAsync(IConfiguration config, bool seedFreshDatabase, ILogger logger)
    {
        var connectionString = config.GetConnectionString("Migrate")
            ?? throw new MigrationException(
                "ConnectionStrings:Migrate is not configured. Migrations need a connection that owns the schema (not workboard_app or workboard_sync).");

        MigrationResult result;
        try
        {
            result = await new Migrator(connectionString, EmbeddedSql.Migrations()).RunAsync(seedFreshDatabase ? EmbeddedSql.Seed() : null);
        }
        catch (NpgsqlException e)
        {
            throw new MigrationException($"Could not run migrations: {e.Message}", e);
        }

        foreach (var name in result.Applied)
            logger.LogInformation("Applied migration {Migration}", name);
        if (result.BaselineAdopted)
            logger.LogInformation("Existing schema adopted as the 0001 baseline");
        if (result.Applied.Count == 0)
            logger.LogInformation("Database schema is up to date");

        if (result.Seeded)
            logger.LogInformation("Loaded sample data into the new database");
        return result;
    }
}
