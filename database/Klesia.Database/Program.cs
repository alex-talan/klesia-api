using System.Reflection;
using Azure.Core;
using Azure.Identity;
using DbUp;
using DbUp.Engine;
using MySqlConnector;

// Applies the KLESIA schema and reference data to an Azure Database for MySQL database.
//
//   KLESIA_DB_DEPLOY_CONNECTION   connection string of the DEPLOYMENT principal. Without a Password the tool
//                                 signs in with Microsoft Entra (az login locally, a managed identity in a
//                                 pipeline) and uses the access token as the password. Never put a password
//                                 in a tracked file or on the command line.
//
// Migrations/  versioned DDL, executed once per database and recorded in the SchemaVersions table.
//              Never edit a script that has been applied anywhere: add a new, higher-numbered one.
// Seeds/       idempotent data scripts, executed after the migrations on EVERY run.

const string ConnectionVariable = "KLESIA_DB_DEPLOY_CONNECTION";
const string EntraScope = "https://ossrdbms-aad.database.windows.net/.default";

string? connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine($"Set the environment variable {ConnectionVariable} (see docs/local-development.md).");
    return 2;
}

MySqlConnectionStringBuilder connection = new(connectionString);
if (string.IsNullOrEmpty(connection.Password))
{
    AccessToken token = await new DefaultAzureCredential().GetTokenAsync(new TokenRequestContext([EntraScope]));
    connection.Password = token.Token;
}

Assembly scripts = Assembly.GetExecutingAssembly();

UpgradeEngine engine = DeployChanges.To
    .MySqlDatabase(connection.ConnectionString)
    .WithScriptsEmbeddedInAssembly(
        scripts,
        name => name.Contains(".Migrations.", StringComparison.Ordinal),
        new SqlScriptOptions { ScriptType = ScriptType.RunOnce, RunGroupOrder = 1 })
    .WithScriptsEmbeddedInAssembly(
        scripts,
        name => name.Contains(".Seeds.", StringComparison.Ordinal),
        new SqlScriptOptions { ScriptType = ScriptType.RunAlways, RunGroupOrder = 2 })
    .LogToConsole()
    .Build();

DatabaseUpgradeResult result = engine.PerformUpgrade();
if (!result.Successful)
{
    Console.Error.WriteLine(result.Error);
    return 1;
}

Console.WriteLine("Database is up to date.");
return 0;
