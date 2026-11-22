using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Klesia.Infrastructure.Options;

namespace Klesia.Infrastructure.Persistence;

public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly DatabaseOptions _options;

    public SqlConnectionFactory(IOptions<DatabaseOptions> options)
    {
        _options = options.Value;
    }

    public IDbConnection CreateConnection()
    {
        return new SqlConnection(_options.KlesiaDb);
    }
}
