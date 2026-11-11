using WorkBoard.Api.Features.Members;
using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.Identity;
using WorkBoard.Api.Tests.Fakes;

namespace WorkBoard.Api.Tests;

public class TenantDbTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    private static TenantDb Create(RecordingConnectionFactory factory, bool signedIn = true)
    {
        var user = new CurrentUser();
        if (signedIn)
            user.Set(new DevIdentity(new Member(Guid.NewGuid(), 1, "haneul-dev", null, null, "admin"), TenantId, "acme-health"));
        return new TenantDb(user, factory);
    }

    [Fact]
    public async Task Sets_tenant_inside_the_transaction_before_the_callers_first_query()
    {
        var factory = new RecordingConnectionFactory();

        await Create(factory).ReadAsync(async s =>
        {
            await s.ExecuteAsync("select 1");
            return 0;
        }, CancellationToken.None);

        Assert.Equal(["open", "begin", "command", "command", "commit"], factory.Events);
        Assert.Equal(TenantDb.SetTenantSql, factory.Commands[0].Sql);
        Assert.Equal(TenantId.ToString(), factory.Commands[0].Parameters["tenantId"]);
        Assert.Equal("select 1", factory.Commands[1].Sql);
    }

    [Fact]
    public async Task Does_not_commit_when_the_work_throws()
    {
        var factory = new RecordingConnectionFactory();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(factory).ReadAsync<int>(
            _ => throw new InvalidOperationException("boom"), CancellationToken.None));

        Assert.DoesNotContain("commit", factory.Events);
    }

    [Fact]
    public async Task Refuses_to_open_a_connection_without_an_authenticated_user()
    {
        var factory = new RecordingConnectionFactory();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(factory, signedIn: false).ReadAsync(_ => Task.FromResult(0), CancellationToken.None));

        Assert.Empty(factory.Events);
    }
}
