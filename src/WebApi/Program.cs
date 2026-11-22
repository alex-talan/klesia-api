using Klesia.Application;
using Klesia.Infrastructure;
using Klesia.Infrastructure.Options;
using Klesia.WebApi.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
});

builder.Services.AddKlesiaOptions(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseKlesiaLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

app.Run();

public partial class Program;
