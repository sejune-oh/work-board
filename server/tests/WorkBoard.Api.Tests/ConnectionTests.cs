using Npgsql;
using Microsoft.AspNetCore.Mvc.Testing;
using WorkBoard.Api.Tests.Fakes;

namespace WorkBoard.Api.Tests;

public class ConnectionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ConnectionTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private const string InsertConnection =
        "insert into github_connections (tenant_id, account_id, account_login, account_type, disconnected_at) " +
        "values (@tid, @account, 'acct', @type, @disconnectedAt)";

    [DbFact]
    public async Task Account_can_be_actively_connected_to_one_tenant_only()
    {
        await using var first = await TestTenant.CreateAsync(_factory);
        await using var second = await TestTenant.CreateAsync(_factory);
        var account = await first.ScalarAsync<long>("select account_id from github_connections where tenant_id = @tid", new { tid = first.Id });

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            second.ExecAsync(InsertConnection, new { tid = second.Id, account, type = "user", disconnectedAt = (DateTime?)null }));

        Assert.Equal("23505", ex.SqlState);
    }

    [DbFact]
    public async Task Account_can_move_to_another_tenant_after_disconnect()
    {
        await using var first = await TestTenant.CreateAsync(_factory);
        await using var second = await TestTenant.CreateAsync(_factory);
        var account = await first.ScalarAsync<long>("select account_id from github_connections where tenant_id = @tid", new { tid = first.Id });
        await first.ExecAsync("update github_connections set disconnected_at = now() where tenant_id = @tid", new { tid = first.Id });

        var rows = await second.ExecAsync(InsertConnection,
            new { tid = second.Id, account, type = "user", disconnectedAt = (DateTime?)null });

        Assert.Equal(1, rows);
    }

    [DbFact]
    public async Task Account_type_must_be_organization_or_user()
    {
        await using var tenant = await TestTenant.CreateAsync(_factory);

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            tenant.ExecAsync(InsertConnection, new { tid = tenant.Id, account = 1L, type = "team", disconnectedAt = (DateTime?)null }));

        Assert.Equal("23514", ex.SqlState);
    }

    [DbFact]
    public async Task Connections_endpoint_lists_the_tenants_own_connections()
    {
        await using var tenant = await TestTenant.CreateAsync(_factory);
        await tenant.ExecAsync(InsertConnection,
            new { tid = tenant.Id, account = 99_000_001L + Random.Shared.Next(1000), type = "user", disconnectedAt = (DateTime?)DateTime.UtcNow });

        var connections = await tenant.GetAsync("/api/connections");

        var list = connections.EnumerateArray().ToList();
        Assert.Equal(["organization", "user"], list.Select(c => c.GetProperty("accountType").GetString()));
        Assert.Null(list[0].GetProperty("disconnectedAt").GetString());
        Assert.NotNull(list[1].GetProperty("disconnectedAt").GetString());
    }
}
