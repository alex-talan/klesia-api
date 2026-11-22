using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Klesia.Infrastructure.Options;

public static class KlesiaOptionsServiceCollectionExtensions
{
    public static IServiceCollection AddKlesiaOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<AzureAdOptions, AzureAdOptionsValidator>(configuration);
        services.AddValidatedOptions<CorsOptions, CorsOptionsValidator>(configuration);
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
        services.AddValidatedOptions<StorageOptions, StorageOptionsValidator>(configuration);
        services.AddValidatedOptions<UploadOptions, UploadOptionsValidator>(configuration);
        services.AddOptions<ExtractionOptions>()
            .Bind(configuration.GetSection(ExtractionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ExtractionOptions>>(
            new ExtractionOptionsValidator(GetDocumentIntelligenceTimeout(configuration)));
        services.AddValidatedOptions<AzureSignalROptions, AzureSignalROptionsValidator>(configuration);

        return services;
    }

    public static IServiceCollection AddDocumentIntelligenceOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddValidatedOptions<DocumentIntelligenceOptions, DocumentIntelligenceOptionsValidator>(configuration);
        return services;
    }

    private static IServiceCollection AddValidatedOptions<TOptions, TValidator>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(SectionName<TOptions>.Value))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TOptions>, TValidator>();
        return services;
    }

    private static int GetDocumentIntelligenceTimeout(IConfiguration configuration)
    {
        var configuredTimeout = configuration[$"{DocumentIntelligenceOptions.SectionName}:TimeoutSeconds"];
        return int.TryParse(configuredTimeout, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeout)
            && timeout > 0
                ? timeout
                : 120;
    }

    private static class SectionName<TOptions>
    {
        public static string Value => typeof(TOptions) == typeof(AzureAdOptions) ? AzureAdOptions.SectionName
            : typeof(TOptions) == typeof(CorsOptions) ? CorsOptions.SectionName
            : typeof(TOptions) == typeof(StorageOptions) ? StorageOptions.SectionName
            : typeof(TOptions) == typeof(UploadOptions) ? UploadOptions.SectionName
            : typeof(TOptions) == typeof(DocumentIntelligenceOptions) ? DocumentIntelligenceOptions.SectionName
            : typeof(TOptions) == typeof(AzureSignalROptions) ? AzureSignalROptions.SectionName
            : throw new InvalidOperationException($"No configuration section is defined for {typeof(TOptions).Name}.");
    }
}