using System.Data.Common;
using Npgsql;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Infrastructure.Data;

public interface IAppConnectionFactory
{
    DbConnection Create();
}

public sealed class NpgsqlAppConnectionFactory([FromKeyedServices(DataKeys.App)] NpgsqlDataSource appSource) : IAppConnectionFactory
{
    public DbConnection Create() => appSource.CreateConnection();
}
