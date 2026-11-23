using Dapper;
using Klesia.Infrastructure.Persistence;

namespace Klesia.Infrastructure.Health;

public sealed class SqlHealthProbe : ISqlHealthProbe
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public SqlHealthProbe(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        await using var connection = (Microsoft.Data.SqlClient.SqlConnection)_connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT 1;", commandTimeout: 5, cancellationToken: cancellationToken));
    }
}
