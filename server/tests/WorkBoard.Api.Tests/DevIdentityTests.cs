using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

    private sealed class FakeMemberLookup : IMemberLookup
    {
        public List<string> Lookups { get; } = [];

        public Task<DevIdentity?> FindByLoginAsync(string githubLogin, CancellationToken ct)
        {
            Lookups.Add(githubLogin);
            DevIdentity? identity = githubLogin == "haneul-dev"
                ? new DevIdentity(new Member(Guid.NewGuid(), 5001, "haneul-dev", "김하늘", null, "admin"), TenantId, "acme-health")
                : null;
            return Task.FromResult(identity);
        }

        public Task<IReadOnlyList<DevUser>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DevUser>>([new DevUser("haneul-dev", "김하늘", "acme-health")]);
    }

    private static (WebApplicationFactory<Program> Factory, FakeMemberLookup Lookup, RecordingConnectionFactory Db) Create(
        string environment = "Development")
    {
        var lookup = new FakeMemberLookup();
        var db = new RecordingConnectionFactory();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
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

    private static HttpRequestMessage Get(string path, string? devUser)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (devUser is not null)
            request.Headers.Add("X-Dev-User", devUser);
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
        Assert.Empty(db.Events);
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

        Assert.Equal([new DevUser("haneul-dev", "김하늘", "acme-health")], users);
    }

    [Fact]
    public async Task Dev_users_is_not_mapped_outside_development()
    {
        var (factory, _, _) = Create("Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/dev/users");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
