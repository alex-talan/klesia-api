using Infrastructure.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Infrastructure.Persistence;

public sealed class SqlConnectionFactory(IOptions<SqlServerOptions> options) : ISqlConnectionFactory
{
    public SqlConnection Create() => new(options.Value.KlesiaDb);
}
