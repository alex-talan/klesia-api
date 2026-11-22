using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Security.Cryptography;

namespace IntegrationTests;

public sealed class KlesiaApiFactory : WebApplicationFactory<Program>
{
    public TestLogSink LogSink { get; } = new();

    public TestTokenFactory Tokens { get; } = new();

    public WebApplicationFactory<Program> WithServices(Action<IServiceCollection> configureServices)
    {
        return WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(configureServices);
        });
    }

    public WebApplicationFactory<Program> Replace<TService>(TService implementation)
        where TService : class
    {
        return WithServices(services =>
        {
            services.RemoveAll<TService>();
            services.AddSingleton(implementation);
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:KlesiaDb"] = "Server=tcp:test.database.windows.net,1433;Database=KlesiaTest;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "11111111-1111-1111-1111-111111111111",
                ["AzureAd:ClientId"] = "22222222-2222-2222-2222-222222222222",
                ["AzureAd:RequiredScope"] = "access_as_user",
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
                ["Storage:BlobServiceUri"] = "https://test.blob.core.windows.net",
                ["Storage:ContainerName"] = "bae",
                ["Upload:MaxPdfSizeMb"] = "10",
                ["DocumentIntelligence:Endpoint"] = "https://test.cognitiveservices.azure.com",
                ["DocumentIntelligence:ModelId"] = "test-bae-model",
                ["DocumentIntelligence:TimeoutSeconds"] = "120",
                ["Extraction:MaxConcurrency"] = "2",
                ["Extraction:QueueCapacity"] = "100",
                ["Extraction:StaleAfterMinutes"] = "15",
                ["Extraction:SweepIntervalMinutes"] = "5",
                ["AzureSignalR:Endpoint"] = "https://test.service.signalr.net",
                ["AzureSignalR:HubName"] = "demands"
            });
        });

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(LogSink);
        });

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(LogSink);

            services.PostConfigure<JwtBearerOptions>(options =>
            {
                var rsa = RSA.Create();
                rsa.ImportParameters(Tokens.SigningKeyParameters);

                var configuration = new OpenIdConnectConfiguration
                {
                    Issuer = TestTokenFactory.Issuer
                };
                configuration.SigningKeys.Add(new RsaSecurityKey(rsa));

                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = TestTokenFactory.Issuer,
                    ValidAudience = TestTokenFactory.Audience,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new RsaSecurityKey(rsa)
                };
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Tokens.Dispose();
            LogSink.Dispose();
        }

        base.Dispose(disposing);
    }
}
