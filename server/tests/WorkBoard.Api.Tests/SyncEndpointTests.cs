using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkBoard.Api.Infrastructure.GitHub;
using WorkBoard.Api.Infrastructure.Sync;
using WorkBoard.Api.Tests.Fakes;

namespace WorkBoard.Api.Tests;

public class SyncEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private sealed class FakeSource : IGitHubSource
    {
        public List<SourceRepository> Repositories { get; set; } = [];
        public Func<SourceRepository, RepositorySnapshot>? Snapshot { get; set; }
        public Exception? ThrowOnList { get; set; }
        public Exception? ThrowOnRead { get; set; }
        public TaskCompletionSource? Gate { get; set; }
        public List<(string Name, DateTimeOffset? Since)> Reads { get; } = [];

        public Task<RepositoryList> ListRepositoriesAsync(long installationId, CancellationToken ct) =>
            ThrowOnList is { } e
                ? Task.FromException<RepositoryList>(e)
                : Task.FromResult(new RepositoryList(Repositories, new RateLimitInfo(4990, null)));

        public async Task<RepositorySnapshot> ReadRepositoryAsync(
            long installationId, string owner, string name, DateTimeOffset? since, CancellationToken ct)
        {
            lock (Reads)
                Reads.Add(($"{owner}/{name}", since));
            if (Gate is { } gate)
                await gate.Task;
            if (ThrowOnRead is { } e)
                throw e;
            var repo = Repositories.Single(r => r.FullName == $"{owner}/{name}");
            return Snapshot?.Invoke(repo) ?? new RepositorySnapshot(repo, [], [], [], [], [], new RateLimitInfo(4980, null));
        }
    }

    private readonly WebApplicationFactory<Program> _factory;

    public SyncEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private HttpClient Client(FakeSource fake, string login, bool configured = true, string clientSecret = "secret") =>
        _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitHub:App:AppId"] = configured ? "123" : "0",
                ["GitHub:App:ClientId"] = configured ? "client" : "",
                ["GitHub:App:ClientSecret"] = configured ? clientSecret : "",
                ["GitHub:App:PrivateKeyPath"] = configured ? "unused.pem" : "",
                ["Sync:Enabled"] = "false",
            }));
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IGitHubSource>();
                s.AddSingleton<IGitHubSource>(fake);
            });
        }).CreateClient().WithDevUser(login);

    private static async Task<(string Login, long UserId)> AdminAsync(TestTenant t)
    {
        var (login, userId) = await t.AddMemberAsync("admin");
        await t.ExecAsync("update members set role = 'admin' where tenant_id = @tid and github_user_id = @userId",
            new { tid = t.Id, userId });
        return (login, userId);
    }

    private static Task ConnectAsync(TestTenant t) =>
        t.ExecAsync("update github_connections set installation_id = @i where id = @id", new { i = TestTenant.NextId(), id = t.ConnectionId });

    private static SourceRepository Repo(TestTenant t, string name) =>
        new(TestTenant.NextId(), name, $"org-{t.Suffix}/{name}", "https://x/" + name, false);

    private static RepositorySnapshot WithIssue(SourceRepository repo, long issueId, int number) =>
        new(repo, [], [], [new SourceIssue(issueId, "n", number, "synced", null, null, "open", null, null, T0, null, T0, "https://x/i",
            null, [], [], null, null)], [], [], new RateLimitInfo(4980, null));

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met in time.");
            await Task.Delay(25);
        }
    }

    private static async Task<JsonElement> StatusAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>("/api/sync/status");

    private static Task WaitIdleAsync(HttpClient client) =>
        WaitAsync(async () => !(await StatusAsync(client)).GetProperty("running").GetBoolean());

    private static async Task<string?> ErrorAsync(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();

    [DbFact]
    public async Task Status_is_disconnected_without_an_installation()
    {
        await using var t = await TestTenant.CreateAsync(_factory);

        var status = await StatusAsync(Client(new FakeSource(), t.ViewerLogin));

        Assert.False(status.GetProperty("connected").GetBoolean());
        Assert.False(status.GetProperty("running").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("lastSyncedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, status.GetProperty("lastResult").ValueKind);
        Assert.Equal(JsonValueKind.Null, status.GetProperty("nextRunAt").ValueKind);
    }

    [DbFact]
    public async Task Status_is_connected_and_idle_before_the_first_run()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        await ConnectAsync(t);

        var status = await StatusAsync(Client(new FakeSource(), t.ViewerLogin));

        Assert.True(status.GetProperty("connected").GetBoolean());
        Assert.False(status.GetProperty("running").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("lastResult").ValueKind);
    }

    [DbFact]
    public async Task Run_requires_an_admin()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        await ConnectAsync(t);

        var response = await Client(new FakeSource(), t.ViewerLogin).PostAsync("/api/sync/run", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("admin_only", await ErrorAsync(response));
    }

    [DbFact]
    public async Task Run_without_an_installation_is_not_connected()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        var (admin, _) = await AdminAsync(t);

        var response = await Client(new FakeSource(), admin).PostAsync("/api/sync/run", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("not_connected", await ErrorAsync(response));
    }

    [DbFact]
    public async Task Run_without_app_settings_is_503()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        await ConnectAsync(t);
        var (admin, _) = await AdminAsync(t);

        var response = await Client(new FakeSource(), admin, configured: false).PostAsync("/api/sync/run", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("github_app_not_configured", await ErrorAsync(response));
    }

    [DbFact]
    public async Task Run_does_not_need_the_client_secret()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        await ConnectAsync(t);
        var (admin, _) = await AdminAsync(t);
        var client = Client(new FakeSource(), admin, clientSecret: "");

        var response = await client.PostAsync("/api/sync/run", null);
        await WaitIdleAsync(client);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [DbFact]
    public async Task A_recreated_repository_does_not_fail_the_run()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        await ConnectAsync(t);
        var (admin, _) = await AdminAsync(t);
        var fake = new FakeSource { Repositories = [Repo(t, "api")] };
        var client = Client(fake, admin);
        await client.PostAsync("/api/sync/run", null);
        await WaitIdleAsync(client);

        fake.Repositories = [Repo(t, "api")];
        await client.PostAsync("/api/sync/run", null);
        await WaitIdleAsync(client);

        Assert.Equal("ok", (await StatusAsync(client)).GetProperty("lastResult").GetString());
    }

    [DbFact]
    public async Task Run_writes_rows_records_the_run_and_shows_in_status()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        await ConnectAsync(t);
        var (admin, _) = await AdminAsync(t);
        var repo = Repo(t, "api");
        var fake = new FakeSource { Repositories = [repo], Snapshot = r => WithIssue(r, TestTenant.NextId(), 1) };
        var client = Client(fake, admin);

        var response = await client.PostAsync("/api/sync/run", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("started").GetBoolean());
        await WaitIdleAsync(client);

        Assert.Equal("synced", await t.ScalarAsync<string>("select title from issues where tenant_id = @tid", new { tid = t.Id }));
        var run = (await t.QueryAsync(
            "select status, issues_synced, rate_limit_remaining, finished_at from sync_runs where tenant_id = @tid", new { tid = t.Id })).Single();
        Assert.Equal("ok", (string)run.status);
        Assert.Equal(1, (int)run.issues_synced);
        Assert.Equal(4980, (int)run.rate_limit_remaining);
        Assert.NotNull(run.finished_at);
        Assert.Null(fake.Reads.Single().Since);

        var status = await StatusAsync(client);
        Assert.Equal("ok", status.GetProperty("lastResult").GetString());
        Assert.Equal(4980, status.GetProperty("rateLimitRemaining").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, status.GetProperty("lastSyncedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, status.GetProperty("lastError").ValueKind);
    }

    [DbFact]
    public async Task Second_run_reads_since_the_last_sync_minus_the_overlap()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        await ConnectAsync(t);
        var (admin, _) = await AdminAsync(t);
        var fake = new FakeSource { Repositories = [Repo(t, "api")] };
        var client = Client(fake, admin);

        await client.PostAsync("/api/sync/run", null);
        await WaitIdleAsync(client);
        var lastSynced = await t.ScalarAsync<DateTime>("select last_synced_at from repositories where tenant_id = @tid", new { tid = t.Id });
        await client.PostAsync("/api/sync/run", null);
        await WaitIdleAsync(client);

        Assert.Equal(2, fake.Reads.Count);
        var since = fake.Reads[1].Since!.Value.UtcDateTime;
        Assert.True(Math.Abs((since - (DateTime.SpecifyKind(lastSynced, DateTimeKind.Utc) - SyncRunner.Overlap)).TotalSeconds) < 1);
    }

    [DbFact]
    public async Task Repositories_with_sync_disabled_are_not_read()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        await ConnectAsync(t);
        var (admin, _) = await AdminAsync(t);
        var on = Repo(t, "on");
        var off = Repo(t, "off");
        var fake = new FakeSource { Repositories = [on, off] };
        var client = Client(fake, admin);
        await client.PostAsync("/api/sync/run", null);
        await WaitIdleAsync(client);
        await t.ExecAsync("update repositories set sync_enabled = false where tenant_id = @tid and github_repo_id = @id", new { tid = t.Id, id = off.Id });
        fake.Reads.Clear();

        await client.PostAsync("/api/sync/run", null);
        await WaitIdleAsync(client);

        Assert.Equal(["on"], fake.Reads.Select(r => r.Name[(r.Name.IndexOf('/') + 1)..]).ToArray());
    }

    [DbFact]
    public async Task Second_run_while_one_is_running_is_rejected()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        await ConnectAsync(t);
        var (admin, _) = await AdminAsync(t);
        var gate = new TaskCompletionSource();
        var fake = new FakeSource { Repositories = [Repo(t, "api")], Gate = gate };
        var client = Client(fake, admin);

        var first = await client.PostAsync("/api/sync/run", null);
        var second = await client.PostAsync("/api/sync/run", null);
        var running = (await StatusAsync(client)).GetProperty("running").GetBoolean();
        gate.SetResult();
        await WaitIdleAsync(client);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("sync_running", await ErrorAsync(second));
        Assert.True(running);
        Assert.Equal(1, await t.ScalarAsync<long>("select count(*) from sync_runs where tenant_id = @tid", new { tid = t.Id }));
    }

    [DbFact]
    public async Task A_github_error_is_recorded_and_the_app_keeps_working()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        await ConnectAsync(t);
        var (admin, _) = await AdminAsync(t);
        var fake = new FakeSource
        {
            Repositories = [Repo(t, "api")],
            ThrowOnRead = new GitHubSourceException(GitHubSourceErrorKind.Forbidden, "no access"),
        };
        var client = Client(fake, admin);

        var response = await client.PostAsync("/api/sync/run", null);
        await WaitIdleAsync(client);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var status = await StatusAsync(client);
        Assert.Equal("failed", status.GetProperty("lastResult").GetString());
        Assert.Contains("Forbidden", status.GetProperty("lastError").GetString());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("lastSyncedAt").ValueKind);
    }

    [DbFact]
    public async Task A_missing_app_key_is_recorded_as_a_failed_run()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        await ConnectAsync(t);
        var (admin, _) = await AdminAsync(t);
        var fake = new FakeSource { ThrowOnList = new GitHubAppKeyException("key unreadable", new IOException()) };
        var client = Client(fake, admin);

        await client.PostAsync("/api/sync/run", null);
        await WaitIdleAsync(client);

        var status = await StatusAsync(client);
        Assert.Equal("failed", status.GetProperty("lastResult").GetString());
        Assert.Equal("key unreadable", status.GetProperty("lastError").GetString());
    }

    [DbFact]
    public async Task One_failing_repository_does_not_stop_the_others()
    {
        await using var t = await TestTenant.CreateAsync(_factory);
        await ConnectAsync(t);
        var (admin, _) = await AdminAsync(t);
        var bad = Repo(t, "a-bad");
        var good = Repo(t, "b-good");
        var fake = new FakeSource
        {
            Repositories = [bad, good],
            Snapshot = r => r.Name == "a-bad"
                ? throw new GitHubSourceException(GitHubSourceErrorKind.GraphQl, "boom")
                : WithIssue(r, TestTenant.NextId(), 1),
        };
        var client = Client(fake, admin);

        await client.PostAsync("/api/sync/run", null);
        await WaitIdleAsync(client);

        Assert.Equal(1, await t.ScalarAsync<long>("select count(*) from issues where tenant_id = @tid", new { tid = t.Id }));
        Assert.Equal("failed", (await StatusAsync(client)).GetProperty("lastResult").GetString());
    }
}
