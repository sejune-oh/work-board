using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WorkBoard.Api.Features.Members;
using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.Identity;
using WorkBoard.Api.Tests.Fakes;

namespace WorkBoard.Api.Tests;

public class BearerAuthTests
{
    private const string SupabaseUrl = "https://test-project.supabase.example";
    private static readonly Guid Acme = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Globex = Guid.Parse("10000000-0000-0000-0000-000000000002");

    private static readonly Guid SingleUser = Guid.NewGuid();
    private static readonly Guid SharedUser = Guid.NewGuid();
    private static readonly Guid LonelyUser = Guid.NewGuid();
    private static readonly Guid UnlinkedUser = Guid.NewGuid();

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "test" };
    private static readonly RsaSecurityKey OtherKey = new(RSA.Create(2048)) { KeyId = "test" };

    private sealed class FakeMembers : IMemberLookup
    {
        public List<string> Lookups { get; } = [];

        public Task<long?> FindUserIdByLoginAsync(string githubLogin, CancellationToken ct)
        {
            Lookups.Add(githubLogin);
            return Task.FromResult<long?>(githubLogin == "dev-user" ? 5001 : null);
        }

        public Task<IReadOnlyList<DevIdentity>> FindByUserIdAsync(long githubUserId, CancellationToken ct)
        {
            IReadOnlyList<DevIdentity> list = githubUserId switch
            {
                5001 => [Identity(5001, Acme, "acme-health")],
                5100 => [Identity(5100, Acme, "acme-health"), Identity(5100, Globex, "globex-labs")],
                _ => [],
            };
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<DevUser>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<DevUser>>([]);

        private static DevIdentity Identity(long id, Guid tenant, string name) =>
            new(new Member(Guid.NewGuid(), id, "login" + id, null, null, "member"), tenant, name);
    }

    private sealed class FakeIdentitySource : IAuthIdentitySource
    {
        public List<Guid> Calls { get; } = [];

        public Task<long?> FindGithubUserIdAsync(Guid authUserId, CancellationToken ct)
        {
            Calls.Add(authUserId);
            long? id = authUserId == SingleUser ? 5001
                : authUserId == SharedUser ? 5100
                : authUserId == LonelyUser ? 5999
                : null;
            return Task.FromResult(id);
        }
    }

    private sealed class FakeLinkStore : IAuthLinkStore
    {
        public Dictionary<Guid, long> Links { get; } = [];

        public Task<long?> FindAsync(Guid authUserId, CancellationToken ct) =>
            Task.FromResult(Links.TryGetValue(authUserId, out var id) ? id : (long?)null);

        public Task SaveAsync(Guid authUserId, long githubUserId, CancellationToken ct)
        {
            Links[authUserId] = githubUserId;
            return Task.CompletedTask;
        }
    }

    private sealed class Host
    {
        public required WebApplicationFactory<Program> Factory { get; init; }
        public required FakeMembers Members { get; init; }
        public required FakeIdentitySource Source { get; init; }
        public required FakeLinkStore Store { get; init; }
        public required RecordingConnectionFactory Db { get; init; }
    }

    private static Host Create(string environment = "Production", bool configured = true)
    {
        var members = new FakeMembers();
        var source = new FakeIdentitySource();
        var store = new FakeLinkStore();
        var db = new RecordingConnectionFactory();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            // Always set every key so a developer's appsettings.Local.json cannot leak into the test.
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Supabase:Url"] = configured ? SupabaseUrl : "",
                ["Auth:Supabase:ServiceRoleKey"] = configured ? "test-service-role-key" : "",
                ["Auth:Supabase:JwtSecret"] = "",
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMemberLookup>();
                services.AddSingleton<IMemberLookup>(members);
                services.RemoveAll<IAuthIdentitySource>();
                services.AddSingleton<IAuthIdentitySource>(source);
                services.RemoveAll<IAuthLinkStore>();
                services.AddSingleton<IAuthLinkStore>(store);
                services.RemoveAll<IAppConnectionFactory>();
                services.AddSingleton<IAppConnectionFactory>(db);
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    o.ConfigurationManager = null;
                    o.TokenValidationParameters.IssuerSigningKey = SigningKey;
                });
            });
        });
        return new Host { Factory = factory, Members = members, Source = source, Store = store, Db = db };
    }

    private static string Token(
        Guid sub, SecurityKey? key = null, string audience = "authenticated", TimeSpan? lifetime = null,
        string issuer = SupabaseUrl + "/auth/v1")
    {
        var life = lifetime ?? TimeSpan.FromHours(1);
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = new Dictionary<string, object> { ["sub"] = sub.ToString() },
            NotBefore = now.AddHours(-3),
            IssuedAt = now.AddHours(-3),
            Expires = now + life,
            SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }

    private static HttpRequestMessage Get(string path, string? token, string? tenant = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        if (tenant is not null)
            request.Headers.Add("X-Tenant", tenant);
        return request;
    }

    private static async Task AssertError(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal($$"""{"error":"{{code}}"}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Valid_token_reaches_the_endpoint_scoped_to_the_members_tenant()
    {
        var host = Create();
        using var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", Token(SingleUser)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Acme.ToString(), host.Db.Commands[0].Parameters["tenantId"]);
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_401()
    {
        var host = Create();
        using var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", Token(SingleUser, OtherKey)));

        await AssertError(response, HttpStatusCode.Unauthorized, "unauthenticated");
        Assert.Empty(host.Source.Calls);
        Assert.Empty(host.Db.Events);
    }

    [Fact]
    public async Task Expired_token_is_401()
    {
        var host = Create();
        using var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", Token(SingleUser, lifetime: TimeSpan.FromHours(-2))));

        await AssertError(response, HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Wrong_audience_is_401()
    {
        var host = Create();
        using var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", Token(SingleUser, audience: "anon")));

        await AssertError(response, HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Wrong_issuer_is_401()
    {
        var host = Create();
        using var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", Token(SingleUser, issuer: "https://evil.example/auth/v1")));

        await AssertError(response, HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Garbage_bearer_is_401()
    {
        var host = Create();
        using var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", "not-a-jwt"));

        await AssertError(response, HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Missing_token_outside_development_is_401()
    {
        var host = Create();
        using var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", null));

        await AssertError(response, HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task User_without_a_github_identity_is_401()
    {
        var host = Create();
        using var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", Token(UnlinkedUser)));

        await AssertError(response, HttpStatusCode.Unauthorized, "unauthenticated");
        Assert.Empty(host.Store.Links);
    }

    [Fact]
    public async Task Valid_login_without_membership_is_403()
    {
        var host = Create();
        using var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", Token(LonelyUser)));

        await AssertError(response, HttpStatusCode.Forbidden, "no_membership");
        Assert.Empty(host.Db.Events);
    }

    [Fact]
    public async Task Two_tenants_without_x_tenant_is_tenant_required()
    {
        var host = Create();
        using var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", Token(SharedUser)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            $$"""{"error":"tenant_required","tenants":[{"id":"{{Acme}}","name":"acme-health"},{"id":"{{Globex}}","name":"globex-labs"}]}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Tenant_required_lists_only_the_callers_own_tenants()
    {
        var host = Create();
        using var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", Token(SharedUser), Guid.NewGuid().ToString()));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("acme-health", body);
        Assert.Contains("globex-labs", body);
        Assert.DoesNotContain("initech", body);
    }

    [Fact]
    public async Task Two_tenants_with_a_foreign_x_tenant_is_tenant_required()
    {
        var host = Create();
        using var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", Token(SharedUser), Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(""""error":"tenant_required"""", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("10000000-0000-0000-0000-000000000001")]
    [InlineData("10000000-0000-0000-0000-000000000002")]
    public async Task Two_tenants_with_x_tenant_scopes_that_tenant(string tenant)
    {
        var host = Create();
        using var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", Token(SharedUser), tenant));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(tenant, host.Db.Commands[0].Parameters["tenantId"]);
    }

    [Fact]
    public async Task First_sight_asks_the_identity_source_and_stores_the_link()
    {
        var host = Create();
        using var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", Token(SingleUser)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([SingleUser], host.Source.Calls);
        Assert.Equal(5001, host.Store.Links[SingleUser]);
    }

    [Fact]
    public async Task Stored_link_skips_the_identity_source()
    {
        var host = Create();
        host.Store.Links[SingleUser] = 5001;
        using var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Get("/api/members", Token(SingleUser)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(host.Source.Calls);
    }

    [Fact]
    public async Task Repeated_requests_ask_the_identity_source_once()
    {
        var host = Create();
        using var client = host.Factory.CreateClient();

        await client.SendAsync(Get("/api/members", Token(SingleUser)));
        await client.SendAsync(Get("/api/members", Token(SingleUser)));

        Assert.Single(host.Source.Calls);
    }

    [Fact]
    public async Task Dev_header_is_ignored_when_authorization_is_present()
    {
        var host = Create("Development");
        using var client = host.Factory.CreateClient();
        var request = Get("/api/members", Token(SingleUser, OtherKey));
        request.Headers.Add("X-Dev-User", "dev-user");

        var response = await client.SendAsync(request);

        await AssertError(response, HttpStatusCode.Unauthorized, "unauthenticated");
        Assert.Empty(host.Members.Lookups);
    }

    [Fact]
    public async Task Dev_header_still_works_in_development_without_authorization()
    {
        var host = Create("Development", configured: false);
        using var client = host.Factory.CreateClient();
        var request = Get("/api/members", null);
        request.Headers.Add("X-Dev-User", "dev-user");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dev_header_is_off_outside_development()
    {
        var host = Create("Production");
        using var client = host.Factory.CreateClient();
        var request = Get("/api/members", null);
        request.Headers.Add("X-Dev-User", "dev-user");

        var response = await client.SendAsync(request);

        await AssertError(response, HttpStatusCode.Unauthorized, "unauthenticated");
        Assert.Empty(host.Members.Lookups);
    }

    [Fact]
    public void Missing_config_outside_development_fails_at_startup()
    {
        var host = Create("Production", configured: false);

        var ex = Assert.ThrowsAny<Exception>(() => host.Factory.CreateClient());

        Assert.Contains("Auth:Supabase:Url", ex.ToString());
    }

    [Fact]
    public async Task Missing_config_in_development_starts_and_rejects_bearer_tokens()
    {
        var host = Create("Development", configured: false);
        using var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Get("/api/health", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Cors_preflight_allows_authorization_and_x_tenant()
    {
        var host = Create();
        using var client = host.Factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/me");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,x-tenant");

        var response = await client.SendAsync(request);

        var allowed = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")).ToLowerInvariant();
        Assert.Contains("authorization", allowed);
        Assert.Contains("x-tenant", allowed);
    }
}

public class AdminIdentityParsingTests
{
    private static long? Parse(string json) =>
        SupabaseAdminIdentitySource.ParseGithubUserId(System.Text.Json.JsonDocument.Parse(json).RootElement);

    [Fact]
    public void Picks_the_github_identity_provider_id()
    {
        Assert.Equal(777, Parse("""
            {"identities":[{"provider":"email","id":"a"},
              {"provider":"github","id":"b1a2","identity_data":{"provider_id":"777","sub":"777"}}],
             "user_metadata":{"provider_id":"1"}}
            """));
    }

    [Fact]
    public void Falls_back_to_sub_and_numeric_id()
    {
        Assert.Equal(42, Parse("""{"identities":[{"provider":"github","identity_data":{"sub":"42"}}]}"""));
        Assert.Equal(43, Parse("""{"identities":[{"provider":"github","id":"43"}]}"""));
    }

    [Fact]
    public void Ignores_user_metadata_and_other_providers()
    {
        Assert.Null(Parse("""{"identities":[{"provider":"google","id":"9"}],"user_metadata":{"provider_id":"5"}}"""));
        Assert.Null(Parse("""{"user_metadata":{"provider_id":"5"}}"""));
    }
}
