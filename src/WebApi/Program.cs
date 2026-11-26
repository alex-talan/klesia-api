using Klesia.Application;
using Klesia.Infrastructure;
using Klesia.Infrastructure.Health;
using Klesia.Infrastructure.Options;
using Klesia.WebApi.Authentication;
using Klesia.WebApi.Cors;
using Klesia.WebApi.Errors;
using Klesia.WebApi.Health;
using Klesia.WebApi.Logging;
using Klesia.WebApi;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using KlesiaCors = Klesia.WebApi.Cors.CorsServiceCollectionExtensions;

using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
});

if (string.Equals(
    Environment.GetEnvironmentVariable("ASPNETCORE_FORWARDEDHEADERS_ENABLED"),
    "true",
    StringComparison.OrdinalIgnoreCase))
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    });
}

builder.Services.AddKlesiaOptions(builder.Configuration);
builder.Services.AddOptions<FormOptions>()
    .Configure<IOptions<UploadOptions>>((options, upload) =>
        options.MultipartBodyLengthLimit = UploadRequestLimits.ForPdfSize(upload.Value.MaxPdfSizeBytes));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddKlesiaCors(builder.Configuration);
builder.Services.AddKlesiaAuthentication(builder.Configuration);
builder.Services.AddScoped<ICurrentPrincipal, CurrentPrincipal>();
builder.Services.AddAuthorization(options =>
{
    var delegatedUser = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new DelegatedUserRequirement())
        .Build();

    options.DefaultPolicy = delegatedUser;
    options.FallbackPolicy = delegatedUser;
});
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, DelegatedUserAuthorizationHandler>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, KlesiaAuthorizationResultHandler>();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions.Remove("exception");
        context.ProblemDetails.Extensions.Remove("instance");

        var correlationId = context.HttpContext.Items.TryGetValue(
            CorrelationIdMiddleware.ItemName,
            out var value)
            ? value?.ToString()
            : context.HttpContext.TraceIdentifier;

        context.ProblemDetails.Extensions["traceId"] = correlationId;
    };
});
builder.Services.AddControllers();


var swaggerTenantId = builder.Configuration["Swagger:TenantId"];
var swaggerClientId = builder.Configuration["Swagger:ClientId"];
var swaggerScope = builder.Configuration["Swagger:Scope"];

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        if (!builder.Environment.IsDevelopment())
        {
            return Task.CompletedTask;
        }

        var authority =
            $"https://login.microsoftonline.com/{swaggerTenantId}/oauth2/v2.0";

        document.Components ??= new OpenApiComponents();

        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes["EntraId"] =
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Description = "Microsoft Entra ID authentication",
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri($"{authority}/authorize"),
                        TokenUrl = new Uri($"{authority}/token"),
                        Scopes = new Dictionary<string, string>
                        {
                            [swaggerScope!] = "Access Klesia API"
                        }
                    }
                }
            };

        document.Security ??= new List<OpenApiSecurityRequirement>();

        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("EntraId", document)] =
                new List<string> { swaggerScope! }
        });

        return Task.CompletedTask;
    });
});



builder.Services.AddHealthChecks().AddCheck<SqlHealthCheck>("sql");

var app = builder.Build();

app.UseForwardedHeaders();
app.UseKlesiaLogging();
app.UseExceptionHandler();

// Swagger UI must be registered here
app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/openapi/v1.json",
            "Klesia API v1"
        );

        options.RoutePrefix = "swagger";

        options.OAuthClientId(swaggerClientId);
        options.OAuthAppName("Klesia Swagger");
        options.OAuthUsePkce();
        options.OAuthScopes(swaggerScope!);
    });

app.UseRouting();
app.UseCors(KlesiaCors.PolicyName);
app.UseAuthentication();
app.UseAuthorization();
app.UseStatusCodePages(async statusContext =>
{
    if (statusContext.HttpContext.Response.HasStarted)
    {
        return;
    }

    var status = statusContext.HttpContext.Response.StatusCode;
    var title = status switch
    {
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status405MethodNotAllowed => "Method Not Allowed",
        StatusCodes.Status415UnsupportedMediaType => "Unsupported Media Type",
        StatusCodes.Status413PayloadTooLarge => "Payload Too Large",
        StatusCodes.Status422UnprocessableEntity => "Unprocessable Content",
        StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
        _ => "An error occurred"
    };

    var result = Results.Json(new
    {
        title,
        status,
        traceId = statusContext.HttpContext.Items.TryGetValue(
            CorrelationIdMiddleware.ItemName,
            out var value)
            ? value?.ToString()
            : statusContext.HttpContext.TraceIdentifier
    }, contentType: "application/problem+json", statusCode: status);

    await result.ExecuteAsync(statusContext.HttpContext);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapControllers();

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = HealthResponseWriter.WriteAsync,
    ResultStatusCodes =
    {
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy] = StatusCodes.Status200OK,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
}).AllowAnonymous();

app.Run();

public partial class Program;
