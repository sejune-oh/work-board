namespace WorkBoard.Api.Infrastructure.Data.Migrations;

// db/migrations/*.sql and db/seed/seed.sql are embedded so the API carries its own schema.
public static class EmbeddedSql
{
    public static IReadOnlyList<SqlMigration> Migrations() => Load("db/migrations/");

    public static string? Seed() => Load("db/seed/").FirstOrDefault()?.Sql;

    private static List<SqlMigration> Load(string prefix)
    {
        var assembly = typeof(EmbeddedSql).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(n =>
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(n)!);
                return new SqlMigration(Path.GetFileNameWithoutExtension(n), reader.ReadToEnd());
            })
            .ToList();
    }
}
