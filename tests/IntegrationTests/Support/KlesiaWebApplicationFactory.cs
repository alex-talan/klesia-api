using Application.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IntegrationTests.Support;

/// <summary>
/// Hosts the real API pipeline (JWT validation, scope policy, CORS, error handling) with in-memory
/// repositories instead of SQL Server and a locally trusted signing key instead of Entra metadata.
/// </summary>
public sealed class KlesiaWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ApprovedOrigin = "http://localhost:5173";

    public FakeUserRepository Users { get; } = new();

    public FakeDemandRepository Demands { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("IntegrationTesting");

        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:KlesiaDb"] = "Server=tcp:unused.invalid,1433;Database=unused",
                ["AzureAd:TenantId"] = TestTokens.TenantId.ToString(),
                ["AzureAd:ClientId"] = TestTokens.ClientId.ToString(),
                ["AzureAd:RequiredScope"] = TestTokens.RequiredScope,
                ["Cors:AllowedOrigins:0"] = ApprovedOrigin
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IUserRepository>();
            services.RemoveAll<IDemandRepository>();
            services.AddSingleton<IUserRepository>(Users);
            services.AddSingleton<IDemandRepository>(Demands);

            services.Configure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options => options.Configuration = TestTokens.BuildConfiguration());
        });
    }
}
