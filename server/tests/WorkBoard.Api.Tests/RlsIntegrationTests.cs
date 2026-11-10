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

    private HttpClient ClientFor(string login, string? tenantId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", login);
        if (tenantId is not null)
            client.DefaultRequestHeaders.Add("X-Dev-Tenant", tenantId);
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

    [DbFact]
    public async Task Login_in_two_tenants_without_tenant_header_is_tenant_required()
    {
        using var client = ClientFor("shared-dev");

        var response = await client.GetAsync("/api/issues");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("""{"error":"tenant_required"}""", await response.Content.ReadAsStringAsync());
    }

    [DbFact]
    public async Task Login_in_two_tenants_sees_only_the_selected_tenants_issues()
    {
        using var globex = ClientFor("shared-dev", "10000000-0000-0000-0000-000000000002");
        using var acme = ClientFor("shared-dev", "10000000-0000-0000-0000-000000000001");

        var globexIssues = await globex.GetFromJsonAsync<JsonElement>("/api/issues");
        var acmeIssues = await acme.GetFromJsonAsync<JsonElement>("/api/issues");

        Assert.Equal([OtherTenantIssueId], globexIssues.EnumerateArray().Select(i => i.GetProperty("id").GetString()));
        var acmeIds = acmeIssues.EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();
        Assert.NotEmpty(acmeIds);
        Assert.DoesNotContain(OtherTenantIssueId, acmeIds);
    }

    [DbFact]
    public async Task Renamed_login_still_resolves_the_same_user_in_both_tenants()
    {
        await using var first = await Fakes.TestTenant.CreateAsync(_factory);
        await using var second = await Fakes.TestTenant.CreateAsync(_factory);
        var (oldLogin, userId) = await first.AddMemberAsync("dual");
        await second.AddMemberAsync("dual", userId);
        var newLogin = "renamed-" + first.Suffix;
        await first.ExecAsync("update members set github_login = @newLogin where github_user_id = @userId", new { newLogin, userId });
        await second.ExecAsync("update members set github_login = @newLogin where github_user_id = @userId", new { newLogin, userId });

        using var noTenant = ClientFor(newLogin);
        using var inFirst = ClientFor(newLogin, first.Id.ToString());
        using var inSecond = ClientFor(newLogin, second.Id.ToString());
        using var stale = ClientFor(oldLogin, first.Id.ToString());

        Assert.Equal(HttpStatusCode.Unauthorized, (await noTenant.GetAsync("/api/me")).StatusCode);
        var meFirst = await inFirst.GetFromJsonAsync<JsonElement>("/api/me");
        var meSecond = await inSecond.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(first.Id.ToString(), meFirst.GetProperty("tenant").GetProperty("id").GetString());
        Assert.Equal(second.Id.ToString(), meSecond.GetProperty("tenant").GetProperty("id").GetString());
        Assert.Equal(userId, meFirst.GetProperty("member").GetProperty("githubUserId").GetInt64());
        Assert.Equal(newLogin, meSecond.GetProperty("member").GetProperty("githubLogin").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.GetAsync("/api/me")).StatusCode);
    }

    [DbFact]
    public async Task Dev_users_lists_one_entry_per_membership()
    {
        using var client = _factory.CreateClient();

        var users = await client.GetFromJsonAsync<JsonElement>("/api/dev/users");

        var shared = users.EnumerateArray().Where(u => u.GetProperty("githubLogin").GetString() == "shared-dev").ToList();
        Assert.Equal(["Acme Health", "Globex Labs"], shared.Select(u => u.GetProperty("tenantName").GetString()));
        Assert.All(shared, u => Assert.Equal(5100, u.GetProperty("githubUserId").GetInt64()));
        Assert.All(shared, u => Assert.True(Guid.TryParse(u.GetProperty("tenantId").GetString(), out _)));
    }
}
