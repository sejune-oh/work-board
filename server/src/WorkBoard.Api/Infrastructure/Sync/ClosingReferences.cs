using System.Text.RegularExpressions;

namespace WorkBoard.Api.Infrastructure.Sync;

// GitHub fills closingIssuesReferences only for pull requests into the default branch, so the
// "Closes #N" keywords in the body are read as well. Only same-repository references (#N) count.
public static partial class ClosingReferences
{
    [GeneratedRegex(@"(?<![\w/])(?:close[sd]?|fix(?:es|ed)?|resolve[sd]?)\s*:?\s+#(?<n>\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Keyword();

    public static IReadOnlySet<int> ParseIssueNumbers(string? body)
    {
        var numbers = new HashSet<int>();
        if (string.IsNullOrEmpty(body))
            return numbers;
        foreach (Match m in Keyword().Matches(body))
            if (int.TryParse(m.Groups["n"].Value, out var n) && n > 0)
                numbers.Add(n);
        return numbers;
    }
}
