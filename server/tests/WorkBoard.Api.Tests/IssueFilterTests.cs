using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WorkBoard.Api.Tests;

public class IssueFilterTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private HttpClient ClientFor(string login)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", login);
        return client;
    }

    [DbTheory]
    [InlineData(0, HttpStatusCode.OK)]
    [InlineData(365, HttpStatusCode.OK)]
    [InlineData(-1, HttpStatusCode.BadRequest)]
    [InlineData(366, HttpStatusCode.BadRequest)]
    [InlineData(int.MaxValue, HttpStatusCode.BadRequest)]
    public async Task Done_within_days_is_bounded_to_0_through_365(int days, HttpStatusCode expected)
    {
        using var client = ClientFor("haneul-dev");

        var response = await client.GetAsync($"/api/issues?doneWithinDays={days}");

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.BadRequest)
            Assert.Equal("""{"error":"invalid_doneWithinDays"}""", await response.Content.ReadAsStringAsync());
    }
}
