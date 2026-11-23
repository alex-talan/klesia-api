using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Klesia.Infrastructure.Health;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Routing;

namespace IntegrationTests;

public sealed class KlesiaApiFactory : WebApplicationFactory<Program>
{
    public TestLogSink LogSink { get; } = new();

    public TestTokenFactory Tokens { get; } = new();

    private readonly string? _previousForwardedHeadersSetting =
        Environment.GetEnvironmentVariable("ASPNETCORE_FORWARDEDHEADERS_ENABLED");
    private readonly string? _previousAzureAdInstance = Environment.GetEnvironmentVariable("AzureAd__Instance");
    private readonly string? _previousAzureAdTenant = Environment.GetEnvironmentVariable("AzureAd__TenantId");
    private readonly string? _previousAzureAdClient = Environment.GetEnvironmentVariable("AzureAd__ClientId");

    public KlesiaApiFactory()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true");
        Environment.SetEnvironmentVariable("AzureAd__Instance", "https://login.microsoftonline.com/");
        Environment.SetEnvironmentVariable("AzureAd__TenantId", TestTokenFactory.TenantId);
        Environment.SetEnvironmentVariable("AzureAd__ClientId", "22222222-2222-2222-2222-222222222222");
    }

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

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(LogSink);
            services.AddControllers().AddApplicationPart(typeof(TestExceptionController).Assembly);

            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var rsa = RSA.Create();
                rsa.ImportParameters(Tokens.SigningKeyParameters);

                var signingKey = new RsaSecurityKey(rsa) { KeyId = TestTokenFactory.SigningKeyId };

                options.ConfigurationManager = null;
                options.Authority = null;
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = TestTokenFactory.Issuer,
                    ValidAudience = TestTokenFactory.Audience,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    IssuerSigningKeys = [signingKey]
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
            Environment.SetEnvironmentVariable(
                "ASPNETCORE_FORWARDEDHEADERS_ENABLED",
                _previousForwardedHeadersSetting);
            Environment.SetEnvironmentVariable("AzureAd__Instance", _previousAzureAdInstance);
            Environment.SetEnvironmentVariable("AzureAd__TenantId", _previousAzureAdTenant);
            Environment.SetEnvironmentVariable("AzureAd__ClientId", _previousAzureAdClient);
        }

        base.Dispose(disposing);
    }

}
