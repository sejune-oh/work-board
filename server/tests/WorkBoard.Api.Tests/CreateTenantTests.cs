using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using WorkBoard.Api.Features.Tenants;
using WorkBoard.Api.Infrastructure.Identity;
using WorkBoard.Api.Tests.Fakes;

namespace WorkBoard.Api.Tests;

public class CreateTenantTests
{
    private const string SupabaseUrl = "https://test-project.supabase.example";
    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "test" };

    private sealed class FakeSource : IAuthIdentitySource
    {
        public Dictionary<Guid, GithubProfile> Profiles { get; } = [];

        public Task<GithubProfile?> FindGithubProfileAsync(Guid authUserId, CancellationToken ct) =>
            Task.FromResult(Profiles.TryGetValue(authUserId, out var p) ? p : null);
    }

    private sealed class MemoryLinks : IAuthLinkStore
    {
        private readonly Dictionary<Guid, long> _links = [];

        public Task<long?> FindAsync(Guid authUserId, CancellationToken ct) =>
            Task.FromResult(_links.TryGetValue(authUserId, out var id) ? id : (long?)null);

        public Task SaveAsync(Guid authUserId, long githubUserId, CancellationToken ct)
        {
            _links[authUserId] = githubUserId;
            return Task.CompletedTask;
        }
    }

    private sealed class Host : IAsyncDisposable
    {
        public required WebApplicationFactory<Program> Factory { get; init; }
        public FakeSource Source { get; } = new();
        public List<long> Users { get; } = [];

        public (Guid AuthUser, long GithubId, string Login) NewPerson()
        {
            var id = TestTenant.NextId();
            var authUser = Guid.NewGuid();
            var login = "creator-" + id;
            Source.Profiles[authUser] = new GithubProfile(id, login, "Creator " + id, "https://avatars.example/" + id);
            Users.Add(id);
            return (authUser, id, login);
        }

        public async ValueTask DisposeAsync()
        {
            var sync = Factory.Services.GetRequiredKeyedService<NpgsqlDataSource>(DataKeys.Sync);
            await using var conn = await sync.OpenConnectionAsync();
            await conn.ExecuteAsync(
                "delete from tenants where id in (select tenant_id from members where github_user_id = any(@users))",
                new { users = Users.ToArray() });
            Factory.Dispose();
        }
    }

    private static Host Create(string environment = "Production")
    {
        return new Host
        {
            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:Supabase:Url"] = SupabaseUrl,
                    ["Auth:Supabase:ServiceRoleKey"] = "test-service-role-key",
                    ["Auth:Supabase:JwtSecret"] = "",
                }));
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IAuthLinkStore>();
                    services.AddSingleton<IAuthLinkStore, MemoryLinks>();
                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                    {
                        o.ConfigurationManager = null;
                        o.TokenValidationParameters.IssuerSigningKey = SigningKey;
                    });
                });
            }),
        };
    }

    // The fake source must exist before the factory builds its services, so it is swapped in afterwards.
    private static HttpClient Client(Host host) =>
        host.Factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IAuthIdentitySource>();
            s.AddSingleton<IAuthIdentitySource>(host.Source);
        })).CreateClient();

    private static string Token(Guid sub)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = SupabaseUrl + "/auth/v1",
            Audience = "authenticated",
            Claims = new Dictionary<string, object> { ["sub"] = sub.ToString() },
            NotBefore = now.AddHours(-1),
            IssuedAt = now.AddHours(-1),
            Expires = now.AddHours(1),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }

    private static HttpRequestMessage Post(string? token, string json, string? devUser = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/tenants")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (token is not null)
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        if (devUser is not null)
            request.Headers.Add("X-Dev-User", devUser);
        return request;
    }

    private static string Name(string name) => JsonSerializer.Serialize(new { name });

    [Fact]
    public async Task Unauthenticated_is_401()
    {
        await using var host = Create();
        using var client = Client(host);

        var response = await client.SendAsync(Post(null, Name("Acme")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("""{"error":"unauthenticated"}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Garbage_token_is_401()
    {
        await using var host = Create();
        using var client = Client(host);

        var response = await client.SendAsync(Post("nope", Name("Acme")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [DbFact]
    public async Task Bearer_person_without_membership_creates_a_tenant_and_becomes_its_admin()
    {
        await using var host = Create();
        var (authUser, githubId, login) = host.NewPerson();
        using var client = Client(host);

        var response = await client.SendAsync(Post(Token(authUser), Name("  Acme Health  ")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Acme Health", body.GetProperty("tenant").GetProperty("name").GetString());
        var member = body.GetProperty("member");
        Assert.Equal("admin", member.GetProperty("role").GetString());
        Assert.Equal(githubId, member.GetProperty("githubUserId").GetInt64());
        Assert.Equal(login, member.GetProperty("githubLogin").GetString());
        Assert.Equal("Creator " + githubId, member.GetProperty("displayName").GetString());
        Assert.Equal("https://avatars.example/" + githubId, member.GetProperty("avatarUrl").GetString());
    }

    [DbFact]
    public async Task Me_succeeds_with_the_new_tenant_in_x_tenant()
    {
        await using var host = Create();
        var (authUser, githubId, login) = host.NewPerson();
        using var client = Client(host);
        var created = await (await client.SendAsync(Post(Token(authUser), Name("Acme")))).Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = created.GetProperty("tenant").GetProperty("id").GetString()!;

        var me = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        me.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Token(authUser));
        me.Headers.Add("X-Tenant", tenantId);
        var response = await client.SendAsync(me);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(tenantId, body.GetProperty("tenant").GetProperty("id").GetString());
        Assert.Equal(login, body.GetProperty("member").GetProperty("githubLogin").GetString());
        Assert.Equal(githubId, body.GetProperty("member").GetProperty("githubUserId").GetInt64());
        Assert.False(body.GetProperty("settings").GetProperty("sprintsEnabled").GetBoolean());
    }

    [DbFact]
    public async Task New_tenant_is_empty_and_cannot_see_other_tenants()
    {
        await using var host = Create();
        await using var other = await TestTenant.CreateAsync(host.Factory);
        var repo = await other.AddRepoAsync("secret");
        await other.AddIssueAsync(repo);
        var (authUser, _, _) = host.NewPerson();
        using var client = Client(host);
        var created = await (await client.SendAsync(Post(Token(authUser), Name("Fresh")))).Content.ReadFromJsonAsync<JsonElement>();

        HttpRequestMessage Authed(string path)
        {
            var r = new HttpRequestMessage(HttpMethod.Get, path);
            r.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Token(authUser));
            r.Headers.Add("X-Tenant", created.GetProperty("tenant").GetProperty("id").GetString());
            return r;
        }

        var issues = await (await client.SendAsync(Authed("/api/issues"))).Content.ReadFromJsonAsync<JsonElement>();
        var members = await (await client.SendAsync(Authed("/api/members"))).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, issues.GetArrayLength());
        Assert.Equal(1, members.GetArrayLength());
    }

    [DbFact]
    public async Task Creating_does_not_change_other_tenants()
    {
        await using var host = Create();
        await using var other = await TestTenant.CreateAsync(host.Factory);
        var before = await other.ScalarAsync<int>("select count(*) from members where tenant_id = @id", new { id = other.Id });
        var (authUser, _, _) = host.NewPerson();
        using var client = Client(host);

        await client.SendAsync(Post(Token(authUser), Name("Fresh")));

        Assert.Equal(before, await other.ScalarAsync<int>("select count(*) from members where tenant_id = @id", new { id = other.Id }));
    }

    [Theory]
    [InlineData("""{"name":""}""")]
    [InlineData("""{"name":"   "}""")]
    [InlineData("""{}""")]
    [InlineData("""{"name":null}""")]
    [InlineData("""{"name":5}""")]
    [InlineData("""not json""")]
    [InlineData("""{"name":"a\nb"}""")]
    public async Task Invalid_names_are_400(string json)
    {
        await using var host = Create();
        var (authUser, _, _) = host.NewPerson();
        using var client = Client(host);

        var response = await client.SendAsync(Post(Token(authUser), json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("""{"error":"invalid_name"}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Name_over_80_chars_is_400()
    {
        await using var host = Create();
        var (authUser, _, _) = host.NewPerson();
        using var client = Client(host);

        var response = await client.SendAsync(Post(Token(authUser), Name(new string('x', TenantsEndpoints.NameMaxLength + 1))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DbFact]
    public async Task Name_of_exactly_80_chars_is_accepted()
    {
        await using var host = Create();
        var (authUser, _, _) = host.NewPerson();
        using var client = Client(host);

        var response = await client.SendAsync(Post(Token(authUser), Name(new string('x', TenantsEndpoints.NameMaxLength))));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [DbFact]
    public async Task Sixth_tenant_is_409_tenant_limit()
    {
        await using var host = Create();
        var (authUser, _, _) = host.NewPerson();
        using var client = Client(host);

        for (var i = 0; i < TenantsEndpoints.MaxTenantsPerPerson; i++)
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(Post(Token(authUser), Name("t" + i)))).StatusCode);
        var response = await client.SendAsync(Post(Token(authUser), Name("one too many")));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("""{"error":"tenant_limit"}""", await response.Content.ReadAsStringAsync());
    }

    [DbFact]
    public async Task Administering_a_tenant_created_by_someone_else_does_not_use_up_the_limit()
    {
        await using var host = Create();
        var (authUser, githubId, _) = host.NewPerson();
        await using var other = await TestTenant.CreateAsync(host.Factory);
        await other.ExecAsync(
            "insert into members (tenant_id, github_user_id, github_login, role) values (@tid, @githubId, @login, 'admin')",
            new { tid = other.Id, githubId, login = "joined-" + githubId });
        using var client = Client(host);

        for (var i = 0; i < TenantsEndpoints.MaxTenantsPerPerson; i++)
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(Post(Token(authUser), Name("t" + i)))).StatusCode);
        var response = await client.SendAsync(Post(Token(authUser), Name("one too many")));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [DbFact]
    public async Task Concurrent_creations_cannot_exceed_the_limit()
    {
        await using var host = Create();
        var (authUser, githubId, _) = host.NewPerson();
        using var client = Client(host);
        var token = Token(authUser);

        var responses = await Task.WhenAll(Enumerable.Range(0, 9).Select(i => client.SendAsync(Post(token, Name("c" + i)))));

        Assert.Equal(TenantsEndpoints.MaxTenantsPerPerson, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(9 - TenantsEndpoints.MaxTenantsPerPerson, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var sync = host.Factory.Services.GetRequiredKeyedService<NpgsqlDataSource>(DataKeys.Sync);
        await using var conn = await sync.OpenConnectionAsync();
        Assert.Equal(TenantsEndpoints.MaxTenantsPerPerson,
            await conn.ExecuteScalarAsync<int>("select count(*) from members where github_user_id = @githubId", new { githubId }));
    }

    [DbFact]
    public async Task Dev_header_with_a_known_login_creates_a_tenant()
    {
        await using var host = Create("Development");
        await using var existing = await TestTenant.CreateAsync(host.Factory);
        host.Users.Add(await existing.ScalarAsync<long>("select github_user_id from members where tenant_id = @id", new { id = existing.Id }));
        using var client = Client(host);

        var response = await client.SendAsync(Post(null, Name("Dev Co"), existing.ViewerLogin));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("admin", body.GetProperty("member").GetProperty("role").GetString());
        Assert.Equal(existing.ViewerLogin, body.GetProperty("member").GetProperty("githubLogin").GetString());
    }

    [DbFact]
    public async Task Dev_header_with_an_unknown_login_is_401()
    {
        await using var host = Create("Development");
        using var client = Client(host);

        var response = await client.SendAsync(Post(null, Name("Dev Co"), "nobody-" + Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dev_header_is_ignored_outside_development()
    {
        await using var host = Create("Production");
        using var client = Client(host);

        var response = await client.SendAsync(Post(null, Name("Dev Co"), "anyone"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
