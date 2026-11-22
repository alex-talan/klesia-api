using System.Data;

namespace Klesia.Infrastructure.Persistence;

public interface ISqlConnectionFactory
{
    public IDbConnection CreateConnection();
}
