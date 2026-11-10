namespace WorkBoard.Api.Features.Overview;

public static class Workload
{
    public const int OverloadMinimum = 4;
    public const double OverloadMedianFactor = 2;

    public static double Median(IReadOnlyCollection<int> values)
    {
        if (values.Count == 0)
            return 0;
        var sorted = values.Order().ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    // inProgressLike: in_progress + in_review issues. teamCounts holds that number for every member.
    public static bool IsOverloaded(int inProgressLike, IReadOnlyCollection<int> teamCounts) =>
        inProgressLike >= OverloadMinimum && inProgressLike >= OverloadMedianFactor * Median(teamCounts);
}
