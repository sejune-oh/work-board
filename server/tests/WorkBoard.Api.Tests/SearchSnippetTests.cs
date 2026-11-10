using WorkBoard.Api.Features.Search;

namespace WorkBoard.Api.Tests;

public class SearchSnippetTests
{
    [Fact]
    public void Short_body_is_returned_whole()
    {
        Assert.Equal("Safari only", SearchEndpoints.Snippet("Safari only", "safari"));
    }

    [Fact]
    public void Long_body_is_trimmed_around_the_match()
    {
        var body = new string('a', 100) + "NEEDLE" + new string('b', 100);

        var snippet = SearchEndpoints.Snippet(body, "needle");

        Assert.Contains("NEEDLE", snippet);
        Assert.StartsWith("…", snippet);
        Assert.EndsWith("…", snippet);
        Assert.True(snippet.Length < body.Length);
    }
}
