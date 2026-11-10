using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Tests;

public class AuthLinkStoreTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthLinkStoreTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [DbFact]
    public async Task Sync_role_stores_and_reads_a_link_and_keeps_the_first_one()
    {
        var source = _factory.Services.GetRequiredKeyedService<NpgsqlDataSource>(DataKeys.Sync);
        var store = new NpgsqlAuthLinkStore(source);
        var user = Guid.NewGuid();
        try
        {
            Assert.Null(await store.FindAsync(user, default));

            await store.SaveAsync(user, 4242, default);
            await store.SaveAsync(user, 9999, default);

            Assert.Equal(4242, await store.FindAsync(user, default));
        }
        finally
        {
            await using var conn = await source.OpenConnectionAsync();
            await conn.ExecuteAsync("delete from auth_links where auth_user_id = @user", new { user });
        }
    }
}
