using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WorkBoard.Api.Infrastructure.GitHub;
using WorkBoard.Api.Tests.Fakes;

namespace WorkBoard.Api.Tests;

public class ConnectGithubTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Code = "good-code";

    private sealed class FakeGitHub : IGitHubAppClient
    {
        public GitHubUser? User { get; set; }
        public List<long> Installations { get; set; } = [];
        public GitHubInstallation? Installation { get; set; }
        public bool CodeValid { get; set; } = true;
        public bool InstallationsRejected { get; set; }
        public Exception? Throw { get; set; }

        public Task<string?> ExchangeCodeAsync(string code, CancellationToken ct) =>
            Throw is { } e ? Task.FromException<string?>(e) : Task.FromResult(CodeValid && code == Code ? "user-token" : null);

        public Task<GitHubUser?> GetUserAsync(string userToken, CancellationToken ct) => Task.FromResult(User);

        public Task<IReadOnlyList<long>?> ListUserInstallationIdsAsync(string userToken, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<long>?>(InstallationsRejected ? null : Installations);

        public Exception? ThrowOnInstallation { get; set; }

        public Task<GitHubInstallation?> GetInstallationAsync(long installationId, CancellationToken ct) =>
            ThrowOnInstallation is { } e ? Task.FromException<GitHubInstallation?>(e) : Task.FromResult(Installation);
    }

    private readonly WebApplicationFactory<Program> _factory;

    public ConnectGithubTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private HttpClient Client(FakeGitHub fake, string login, bool configured = true) =>
        _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitHub:App:AppId"] = configured ? "123" : "0",
                ["GitHub:App:ClientId"] = configured ? "client" : "",
                ["GitHub:App:ClientSecret"] = configured ? "secret" : "",
                ["GitHub:App:PrivateKeyPath"] = configured ? "unused.pem" : "",
            }));
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IGitHubAppClient>();
                s.AddSingleton<IGitHubAppClient>(fake);
            });
        }).CreateClient().WithDevUser(login);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, long installationId, string code = Code) =>
        client.PostAsJsonAsync("/api/connections/github", new { code, installationId });

    private static async Task<string?> ErrorAsync(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();

    // An admin who owns a personal GitHub account that has an App installation.
    private static async Task<(string Login, FakeGitHub Fake, long InstallationId, long AccountId)> AdminAsync(TestTenant tenant)
    {
        var (login, userId) = await tenant.AddMemberAsync("admin");
        await tenant.ExecAsync("update members set role = 'admin' where tenant_id = @tid and github_user_id = @userId",
            new { tid = tenant.Id, userId });
        var installationId = TestTenant.NextId();
        var accountId = userId;
        var fake = new FakeGitHub
        {
            User = new GitHubUser(userId, login),
            Installations = [installationId],
            Installation = new GitHubInstallation(accountId, login, "User", "selected"),
        };
        return (login, fake, installationId, accountId);
    }

    [DbFact]
    public async Task Admin_connects_personal_account_and_the_row_is_stored()
    {
        await using var tenant = await TestTenant.CreateAsync(_factory);
        var (login, fake, installationId, accountId) = await AdminAsync(tenant);

        var response = await PostAsync(Client(fake, login), installationId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(accountId, body.GetProperty("accountId").GetInt64());
        Assert.Equal("user", body.GetProperty("accountType").GetString());
        Assert.Equal("selected", body.GetProperty("repositorySelection").GetString());
        Assert.Equal(installationId, body.GetProperty("installationId").GetInt64());
        var stored = await tenant.ScalarAsync<long>(
            "select installation_id from github_connections where tenant_id = @tid and account_id = @accountId",
            new { tid = tenant.Id, accountId });
        Assert.Equal(installationId, stored);
        await tenant.ExecAsync("delete from github_connections where account_id = @accountId", new { accountId });
    }

    [DbFact]
    public async Task Reconnecting_the_same_account_updates_the_row()
    {
        await using var tenant = await TestTenant.CreateAsync(_factory);
        var (login, fake, installationId, accountId) = await AdminAsync(tenant);
        await PostAsync(Client(fake, login), installationId);
        var newInstallation = TestTenant.NextId();
        fake.Installations = [newInstallation];
        fake.Installation = new GitHubInstallation(accountId, login, "User", "all");

        var response = await PostAsync(Client(fake, login), newInstallation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await tenant.ScalarAsync<int>(
            "select count(*) from github_connections where account_id = @accountId", new { accountId });
        Assert.Equal(1, rows);
        var selection = await tenant.ScalarAsync<string>(
            "select repository_selection from github_connections where account_id = @accountId", new { accountId });
        Assert.Equal("all", selection);
        await tenant.ExecAsync("delete from github_connections where account_id = @accountId", new { accountId });
    }

    [DbFact]
    public async Task Member_who_is_not_admin_gets_403()
    {
        await using var tenant = await TestTenant.CreateAsync(_factory);
        var fake = new FakeGitHub();

        var response = await PostAsync(Client(fake, tenant.ViewerLogin), 1);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("admin_only", await ErrorAsync(response));
    }

    [DbFact]
    public async Task Token_of_another_github_user_gets_403_not_your_account()
    {
        await using var tenant = await TestTenant.CreateAsync(_factory);
        var (login, fake, installationId, _) = await AdminAsync(tenant);
        fake.User = new GitHubUser(TestTenant.NextId(), "someone-else");

        var response = await PostAsync(Client(fake, login), installationId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("not_your_account", await ErrorAsync(response));
    }

    [DbFact]
    public async Task Installation_outside_the_users_list_gets_403()
    {
        await using var tenant = await TestTenant.CreateAsync(_factory);
        var (login, fake, installationId, _) = await AdminAsync(tenant);
        fake.Installations = [installationId + 1];

        var response = await PostAsync(Client(fake, login), installationId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("not_your_installation", await ErrorAsync(response));
    }

    [DbFact]
    public async Task Personal_installation_of_another_account_gets_403_not_your_account()
    {
        await using var tenant = await TestTenant.CreateAsync(_factory);
        var (login, fake, installationId, _) = await AdminAsync(tenant);
        fake.Installation = new GitHubInstallation(TestTenant.NextId(), "someone-else", "User", "all");

        var response = await PostAsync(Client(fake, login), installationId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("not_your_account", await ErrorAsync(response));
    }

    [DbFact]
    public async Task Invalid_code_gets_400()
    {
        await using var tenant = await TestTenant.CreateAsync(_factory);
        var (login, fake, installationId, _) = await AdminAsync(tenant);

        var response = await PostAsync(Client(fake, login), installationId, "bad");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_code", await ErrorAsync(response));
    }

    [DbFact]
    public async Task Organization_installation_gets_422()
    {
        await using var tenant = await TestTenant.CreateAsync(_factory);
        var (login, fake, installationId, _) = await AdminAsync(tenant);
        fake.Installation = new GitHubInstallation(TestTenant.NextId(), "some-org", "Organization", "all");

        var response = await PostAsync(Client(fake, login), installationId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("organization_not_supported", await ErrorAsync(response));
    }

    [DbFact]
    public async Task Account_connected_to_another_tenant_gets_409()
    {
        await using var tenant = await TestTenant.CreateAsync(_factory);
        await using var other = await TestTenant.CreateAsync(_factory);
        var (login, fake, installationId, accountId) = await AdminAsync(tenant);
        await other.ExecAsync(
            "insert into github_connections (tenant_id, account_id, account_login, account_type) values (@tid, @accountId, 'taken', 'user')",
            new { tid = other.Id, accountId });

        var response = await PostAsync(Client(fake, login), installationId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("account_connected", await ErrorAsync(response));
    }

    [DbFact]
    public async Task Missing_app_configuration_gets_503()
    {
        await using var tenant = await TestTenant.CreateAsync(_factory);
        var (login, fake, installationId, _) = await AdminAsync(tenant);

        var response = await PostAsync(Client(fake, login, configured: false), installationId);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("github_app_not_configured", await ErrorAsync(response));
    }

    [DbFact]
    public async Task Unauthenticated_request_gets_401()
    {
        using var client = _factory.CreateClient();

        var response = await PostAsync(client, 1);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task App_jwt_is_rs256_issued_by_the_app_id_and_expires_within_ten_minutes()
    {
        using var rsa = RSA.Create(2048);
        var now = DateTimeOffset.UtcNow;

        var jwt = new JsonWebToken(GitHubAppJwt.Create(123, rsa, now));

        Assert.Equal("RS256", jwt.Alg);
        Assert.Equal("123", jwt.Issuer);
        var lifetime = jwt.ValidTo - jwt.IssuedAt;
        Assert.True(lifetime <= TimeSpan.FromMinutes(10));
        Assert.True(jwt.IssuedAt <= now.UtcDateTime);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(jwt.EncodedToken, new TokenValidationParameters
        {
            IssuerSigningKey = new RsaSecurityKey(rsa.ExportParameters(false)),
            ValidIssuer = "123",
            ValidateAudience = false,
        });
        Assert.True(result.IsValid);
    }

    public static TheoryData<string> GitHubFailures => ["unreachable", "timeout", "not-json"];

    [DbTheory]
    [MemberData(nameof(GitHubFailures))]
    public async Task GitHub_failures_are_502(string failure)
    {
        await using var tenant = await TestTenant.CreateAsync(_factory);
        var (login, fake, installationId, _) = await AdminAsync(tenant);
        fake.Throw = failure switch
        {
            "unreachable" => new HttpRequestException("down"),
            "timeout" => new TaskCanceledException("timed out"),
            _ => new JsonException("html page"),
        };

        var response = await PostAsync(Client(fake, login), installationId);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("github_unavailable", await ErrorAsync(response));
    }

    [DbFact]
    public async Task Unreadable_private_key_is_503_not_configured()
    {
        await using var tenant = await TestTenant.CreateAsync(_factory);
        var (login, fake, installationId, _) = await AdminAsync(tenant);
        fake.ThrowOnInstallation = new GitHubAppKeyException("bad key", new IOException("missing"));

        var response = await PostAsync(Client(fake, login), installationId);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("github_app_not_configured", await ErrorAsync(response));
    }

    [DbFact]
    public async Task Rejected_installation_list_is_invalid_code()
    {
        await using var tenant = await TestTenant.CreateAsync(_factory);
        var (login, fake, installationId, _) = await AdminAsync(tenant);
        fake.InstallationsRejected = true;

        var response = await PostAsync(Client(fake, login), installationId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_code", await ErrorAsync(response));
    }

    [DbFact]
    public async Task Missing_installation_id_is_invalid_request()
    {
        await using var tenant = await TestTenant.CreateAsync(_factory);
        var (login, fake, _, _) = await AdminAsync(tenant);

        var response = await Client(fake, login).PostAsJsonAsync("/api/connections/github", new { code = Code });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", await ErrorAsync(response));
    }

    [Theory]
    [InlineData("does-not-exist.pem", null)]
    [InlineData("not-a-key.pem", "hello")]
    public void Private_key_that_cannot_be_read_raises_a_key_error(string file, string? content)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        if (content is not null)
            File.WriteAllText(Path.Combine(dir, file), content);
        var key = new GitHubAppKey(
            Microsoft.Extensions.Options.Options.Create(new GitHubAppOptions { PrivateKeyPath = file }),
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { ContentRootPath = dir });

        Assert.Throws<GitHubAppKeyException>(() => key.Get());
    }

    [Fact]
    public void Private_key_is_read_from_a_pem_file()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        using var rsa = RSA.Create(2048);
        File.WriteAllText(Path.Combine(dir, "app.pem"), rsa.ExportRSAPrivateKeyPem());
        var key = new GitHubAppKey(
            Microsoft.Extensions.Options.Options.Create(new GitHubAppOptions { PrivateKeyPath = "app.pem" }),
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { ContentRootPath = dir });

        Assert.Same(key.Get(), key.Get());
    }
}

internal static class ClientExtensions
{
    public static HttpClient WithDevUser(this HttpClient client, string login)
    {
        client.DefaultRequestHeaders.Add("X-Dev-User", login);
        return client;
    }
}
