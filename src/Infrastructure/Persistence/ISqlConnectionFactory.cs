using Microsoft.Data.SqlClient;

namespace Infrastructure.Persistence;

public interface ISqlConnectionFactory
{
    /// <summary>Creates a new, closed connection. The caller owns it and must dispose it.</summary>
    SqlConnection Create();
}
