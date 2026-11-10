using WorkBoard.Api.Features.Overview;

namespace WorkBoard.Api.Tests;

public class WorkloadTests
{
    [Theory]
    [InlineData(new int[0], 0)]
    [InlineData(new[] { 3 }, 3)]
    [InlineData(new[] { 1, 5 }, 3)]
    [InlineData(new[] { 5, 1, 2 }, 2)]
    [InlineData(new[] { 0, 0, 4, 6 }, 2)]
    public void Median_handles_odd_even_and_empty(int[] values, double expected) =>
        Assert.Equal(expected, Workload.Median(values));

    [Theory]
    [InlineData(4, new[] { 4, 0, 0, 0 }, true)]      // median 0
    [InlineData(4, new[] { 4, 2, 2, 2 }, true)]      // exactly 2x the median
    [InlineData(4, new[] { 4, 3, 2 }, false)]        // median 3, needs 6
    [InlineData(3, new[] { 3, 0, 0, 0 }, false)]     // below the absolute floor
    [InlineData(5, new[] { 5, 3, 2, 2 }, true)]      // median 2.5, exactly 5 needed
    [InlineData(4, new[] { 4, 4, 4, 4 }, false)]     // everyone is busy
    [InlineData(4, new[] { 4 }, false)]              // alone: median is itself
    public void Overloaded_needs_four_and_twice_the_median(int mine, int[] team, bool expected)
    {
        Assert.Equal(expected, Workload.IsOverloaded(mine, team));
    }
}
