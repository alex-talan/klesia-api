namespace IntegrationTests.Database;

/// <summary>Runs only when KLESIA_TEST_SQL_CONNECTION points to a published, disposable Klesia test database.</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string ConnectionVariable = "KLESIA_TEST_SQL_CONNECTION";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
        {
            Skip = $"Set {ConnectionVariable} to a published Klesia test database to run the SQL constraint tests.";
        }
    }
}
