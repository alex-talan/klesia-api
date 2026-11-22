using Klesia.Application;
using Klesia.Infrastructure;
using Klesia.Infrastructure.Options;
using Klesia.WebApi.Cors;
using Klesia.WebApi.Errors;
using Klesia.WebApi.Logging;
using Microsoft.AspNetCore.HttpOverrides;
using KlesiaCors = Klesia.WebApi.Cors.CorsServiceCollectionExtensions;

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
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddKlesiaCors(builder.Configuration);
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
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseKlesiaLogging();
app.UseExceptionHandler();
app.UseRouting();
app.UseCors(KlesiaCors.PolicyName);
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
    app.MapOpenApi();
}

app.MapControllers();

app.Run();

public partial class Program;
