namespace WorkBoard.Api.Infrastructure.Sync;

public sealed class SyncOptions
{
    public const string Section = "Sync";

    // Turns the periodic loop off. Manual runs through POST /api/sync/run still work.
    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 10;

    public TimeSpan Interval => TimeSpan.FromMinutes(Math.Max(1, IntervalMinutes));
}
