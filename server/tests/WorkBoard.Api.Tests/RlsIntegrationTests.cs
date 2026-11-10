using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WorkBoard.Api.Tests;

public sealed class DbFactAttribute : FactAttribute
{
    public const string EnvVar = "WORKBOARD_TEST_DB";

    public DbFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvVar)))
            Skip = $"Set {EnvVar}=1 with the seeded Postgres from db/ running to enable.";
    }
}

public sealed class DbTheoryAttribute : TheoryAttribute
{
    public DbTheoryAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DbFactAttribute.EnvVar)))
            Skip = $"Set {DbFactAttribute.EnvVar}=1 with the seeded Postgres from db/ running to enable.";
    }
}

public class RlsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string OtherTenantIssueId = "50000000-0000-0000-0000-000000000099";

    private readonly WebApplicationFactory<Program> _factory;

    public RlsIntegrationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private HttpClient ClientFor(string login)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", login);
        return client;
    }

    [DbFact]
    public async Task Issue_list_never_contains_another_tenants_issue()
    {
        using var client = ClientFor("haneul-dev");

        var issues = await client.GetFromJsonAsync<JsonElement>("/api/issues");

        var ids = issues.EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();
        Assert.NotEmpty(ids);
        Assert.DoesNotContain(OtherTenantIssueId, ids);
    }

    [DbFact]
    public async Task Other_tenants_issue_detail_is_404()
    {
        using var client = ClientFor("haneul-dev");

        var response = await client.GetAsync($"/api/issues/{OtherTenantIssueId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [DbFact]
    public async Task Other_tenant_member_sees_only_their_own_issue()
    {
        using var client = ClientFor("other-org-dev");

        var issues = await client.GetFromJsonAsync<JsonElement>("/api/issues");

        var ids = issues.EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();
        Assert.Equal([OtherTenantIssueId], ids);
    }

    [DbFact]
    public async Task Members_are_limited_to_the_callers_tenant()
    {
        using var client = ClientFor("haneul-dev");

        var members = await client.GetFromJsonAsync<JsonElement>("/api/members");

        var logins = members.EnumerateArray().Select(m => m.GetProperty("githubLogin").GetString()).ToList();
        Assert.Contains("haneul-dev", logins);
        Assert.DoesNotContain("other-org-dev", logins);
    }
}
