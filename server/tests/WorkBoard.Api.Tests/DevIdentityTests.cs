using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkBoard.Api.Features.Members;
using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.Identity;
using WorkBoard.Api.Tests.Fakes;

namespace WorkBoard.Api.Tests;

public class DevIdentityTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    private static readonly Guid GlobexId = Guid.Parse("10000000-0000-0000-0000-000000000002");

    private sealed class FakeMemberLookup : IMemberLookup
    {
        public List<string> Lookups { get; } = [];

        public List<long> IdLookups { get; } = [];

        public Task<long?> FindUserIdByLoginAsync(string githubLogin, CancellationToken ct)
        {
            Lookups.Add(githubLogin);
            long? id = githubLogin switch
            {
                "haneul-dev" => 5001,
                "shared-dev" or "shared-dev-renamed" => 5100,
                _ => null,
            };
            return Task.FromResult(id);
        }

        public Task<IReadOnlyList<DevIdentity>> FindByUserIdAsync(long githubUserId, CancellationToken ct)
        {
            IdLookups.Add(githubUserId);
            IReadOnlyList<DevIdentity> identities = githubUserId switch
            {
                5001 => [new DevIdentity(new Member(Guid.NewGuid(), 5001, "haneul-dev", "김하늘", null, "admin"), TenantId, "acme-health")],
                5100 =>
                [
                    new DevIdentity(new Member(Guid.NewGuid(), 5100, "shared-dev-renamed", null, null, "member"), TenantId, "acme-health"),
                    new DevIdentity(new Member(Guid.NewGuid(), 5100, "shared-dev-renamed", null, null, "member"), GlobexId, "globex-labs"),
                ],
                _ => [],
            };
            return Task.FromResult(identities);
        }

        public Task<IReadOnlyList<DevUser>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DevUser>>([new DevUser("haneul-dev", 5001, "김하늘", TenantId, "acme-health")]);
    }

    private static (WebApplicationFactory<Program> Factory, FakeMemberLookup Lookup, RecordingConnectionFactory Db) Create(
        string environment = "Development")
    {
        var lookup = new FakeMemberLookup();
        var db = new RecordingConnectionFactory();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Supabase:Url"] = "https://test-project.supabase.example",
                ["Auth:Supabase:ServiceRoleKey"] = "test-service-role-key",
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMemberLookup>();
                services.AddSingleton<IMemberLookup>(lookup);
                services.RemoveAll<IAppConnectionFactory>();
                services.AddSingleton<IAppConnectionFactory>(db);
            });
        });
        return (factory, lookup, db);
    }

    private static HttpRequestMessage Get(string path, string? devUser, string? devTenant = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (devUser is not null)
            request.Headers.Add("X-Dev-User", devUser);
        if (devTenant is not null)
            request.Headers.Add("X-Dev-Tenant", devTenant);
        return request;
    }

    [Fact]
    public async Task Missing_header_is_401()
    {
        var (factory, lookup, db) = Create();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get("/api/me", null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("""{"error":"unauthenticated"}""", await response.Content.ReadAsStringAsync());
        Assert.Empty(lookup.Lookups);
        Assert.Empty(db.Events);
    }

    [Fact]
    public async Task Unknown_login_is_401()
    {
        var (factory, lookup, db) = Create();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get("/api/issues", "nobody"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(["nobody"], lookup.Lookups);
        Assert.Empty(lookup.IdLookups);
        Assert.Empty(db.Events);
    }

    [Fact]
    public async Task Login_is_resolved_to_a_user_id_once_and_the_rest_continues_by_id()
    {
        var (factory, lookup, _) = Create();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", "haneul-dev"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["haneul-dev"], lookup.Lookups);
        Assert.Equal([5001L], lookup.IdLookups);
    }

    [Fact]
    public async Task Known_login_reaches_the_endpoint_and_scopes_the_tenant_first()
    {
        var (factory, _, db) = Create();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", "haneul-dev"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TenantDb.SetTenantSql, db.Commands[0].Sql);
        Assert.Equal(TenantId.ToString(), db.Commands[0].Parameters["tenantId"]);
    }

    [Fact]
    public async Task Dev_header_is_ignored_outside_development()
    {
        var (factory, lookup, _) = Create("Production");
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", "haneul-dev"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(lookup.Lookups);
    }

    [Fact]
    public async Task Health_needs_no_identity()
    {
        var (factory, _, _) = Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"status":"ok"}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Dev_users_lists_logins_without_identity_in_development()
    {
        var (factory, _, _) = Create();
        using var client = factory.CreateClient();

        var users = await client.GetFromJsonAsync<List<DevUser>>("/api/dev/users");

        Assert.Equal([new DevUser("haneul-dev", 5001, "김하늘", TenantId, "acme-health")], users);
    }

    [Fact]
    public async Task Dev_users_is_not_mapped_outside_development()
    {
        var (factory, _, _) = Create("Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/dev/users");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Multi_tenant_login_without_tenant_header_is_tenant_required()
    {
        var (factory, _, db) = Create();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", "shared-dev"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("""{"error":"tenant_required"}""", await response.Content.ReadAsStringAsync());
        Assert.Empty(db.Events);
    }

    [Fact]
    public async Task Multi_tenant_login_with_unmatched_tenant_header_is_tenant_required()
    {
        var (factory, _, _) = Create();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", "shared-dev", Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("""{"error":"tenant_required"}""", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("10000000-0000-0000-0000-000000000001")]
    [InlineData("10000000-0000-0000-0000-000000000002")]
    public async Task Multi_tenant_login_with_tenant_header_scopes_that_tenant(string tenantId)
    {
        var (factory, _, db) = Create();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", "shared-dev", tenantId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(tenantId, db.Commands[0].Parameters["tenantId"]);
    }

    [Fact]
    public async Task Single_tenant_login_ignores_the_tenant_header()
    {
        var (factory, _, db) = Create();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", "haneul-dev", GlobexId.ToString()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TenantId.ToString(), db.Commands[0].Parameters["tenantId"]);
    }
}
