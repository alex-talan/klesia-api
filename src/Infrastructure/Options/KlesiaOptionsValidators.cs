using System.Globalization;
using System.Net;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Klesia.Infrastructure.Options;

internal static class OptionValidation
{
    public static bool IsMissingOrPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains('<') || value.Contains('>');

    public static bool IsHttpsUri(string? value, bool requireRoot = false)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return !requireRoot || uri.AbsolutePath == "/";
    }

    public static void RequireValue(ICollection<string> failures, string key, string? value)
    {
        if (IsMissingOrPlaceholder(value))
        {
            failures.Add($"{key} is missing or a placeholder.");
        }
    }

    public static void RequireHttpsUri(ICollection<string> failures, string key, string? value, bool requireRoot = false)
    {
        if (IsMissingOrPlaceholder(value) || !IsHttpsUri(value, requireRoot))
        {
            failures.Add($"{key} must be a valid HTTPS URI without a query or fragment.");
        }
    }

    public static ValidateOptionsResult Result(IReadOnlyCollection<string> failures) =>
        failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
}

public sealed class AzureAdOptionsValidator : IValidateOptions<AzureAdOptions>
{
    public ValidateOptionsResult Validate(string? name, AzureAdOptions options)
    {
        var failures = new List<string>();
        OptionValidation.RequireHttpsUri(failures, "AzureAd:Instance", options.Instance, requireRoot: true);

        if (!Guid.TryParseExact(options.TenantId, "D", out _))
        {
            failures.Add("AzureAd:TenantId must be a GUID.");
        }

        if (!Guid.TryParseExact(options.ClientId, "D", out _))
        {
            failures.Add("AzureAd:ClientId must be a GUID.");
        }

        if (!string.Equals(options.RequiredScope, "access_as_user", StringComparison.Ordinal))
        {
            failures.Add("AzureAd:RequiredScope must be access_as_user.");
        }

        if (options.Audiences?.Any(OptionValidation.IsMissingOrPlaceholder) == true)
        {
            failures.Add("AzureAd:Audiences must not contain empty values or placeholders.");
        }

        return OptionValidation.Result(failures);
    }
}

public sealed class CorsOptionsValidator : IValidateOptions<CorsOptions>
{
    public ValidateOptionsResult Validate(string? name, CorsOptions options)
    {
        var failures = new List<string>();
        foreach (var origin in options.AllowedOrigins ?? [])
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)
                || uri.AbsolutePath != "/"
                || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && IsAllowedLoopback(uri.Host))))
            {
                failures.Add("Cors:AllowedOrigins must contain exact HTTPS origins, except loopback HTTP origins.");
            }
        }

        return OptionValidation.Result(failures);
    }

    private static bool IsAllowedLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host, out var address) && address.Equals(IPAddress.Loopback));
}

public sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        var failures = new List<string>();
        OptionValidation.RequireValue(failures, "ConnectionStrings:KlesiaDb", options.KlesiaDb);
        if (failures.Count > 0)
        {
            return OptionValidation.Result(failures);
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(options.KlesiaDb);
            if (!string.IsNullOrEmpty(builder.Password))
            {
                failures.Add("ConnectionStrings:KlesiaDb must not contain a password.");
            }

            if (builder.Authentication != SqlAuthenticationMethod.ActiveDirectoryDefault)
            {
                failures.Add("ConnectionStrings:KlesiaDb must use Active Directory Default authentication.");
            }
        }
        catch (ArgumentException)
        {
            failures.Add("ConnectionStrings:KlesiaDb must be a valid connection string.");
        }

        return OptionValidation.Result(failures);
    }
}

public sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        var failures = new List<string>();
        OptionValidation.RequireHttpsUri(failures, "Storage:BlobServiceUri", options.BlobServiceUri, requireRoot: true);

        var container = options.ContainerName;
        if (OptionValidation.IsMissingOrPlaceholder(container)
            || container.Length is < 3 or > 63
            || !IsValidContainerName(container))
        {
            failures.Add("Storage:ContainerName must be a valid Azure Blob container name.");
        }

        return OptionValidation.Result(failures);
    }

    private static bool IsValidContainerName(string name)
    {
        if (!IsLowercaseAlphaNumeric(name[0]) || !IsLowercaseAlphaNumeric(name[^1]))
        {
            return false;
        }

        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (!IsLowercaseAlphaNumeric(character) && character != '-')
            {
                return false;
            }

            if (character == '-' && index > 0 && name[index - 1] == '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLowercaseAlphaNumeric(char value) =>
        value is >= 'a' and <= 'z' or >= '0' and <= '9';
}

public sealed class UploadOptionsValidator : IValidateOptions<UploadOptions>
{
    public ValidateOptionsResult Validate(string? name, UploadOptions options) =>
        options.MaxPdfSizeMb > 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Upload:MaxPdfSizeMb must be greater than zero.");
}

public sealed class DocumentIntelligenceOptionsValidator : IValidateOptions<DocumentIntelligenceOptions>
{
    public ValidateOptionsResult Validate(string? name, DocumentIntelligenceOptions options)
    {
        var failures = new List<string>();
        OptionValidation.RequireHttpsUri(failures, "DocumentIntelligence:Endpoint", options.Endpoint, requireRoot: true);
        OptionValidation.RequireValue(failures, "DocumentIntelligence:ModelId", options.ModelId);
        if (options.TimeoutSeconds <= 0)
        {
            failures.Add("DocumentIntelligence:TimeoutSeconds must be greater than zero.");
        }

        if (options.ApiVersion is not null && OptionValidation.IsMissingOrPlaceholder(options.ApiVersion))
        {
            failures.Add("DocumentIntelligence:ApiVersion must not be a placeholder when specified.");
        }

        return OptionValidation.Result(failures);
    }
}

public sealed class ExtractionOptionsValidator(int documentIntelligenceTimeoutSeconds = 120)
    : IValidateOptions<ExtractionOptions>
{
    public ValidateOptionsResult Validate(string? name, ExtractionOptions options)
    {
        var failures = new List<string>();
        if (options.MaxConcurrency < 1)
        {
            failures.Add("Extraction:MaxConcurrency must be at least one.");
        }

        if (options.QueueCapacity < 1)
        {
            failures.Add("Extraction:QueueCapacity must be at least one.");
        }

        var timeoutMinutes = (decimal)documentIntelligenceTimeoutSeconds / 60;
        if (options.StaleAfterMinutes <= timeoutMinutes)
        {
            failures.Add("Extraction:StaleAfterMinutes must be greater than the Document Intelligence timeout in minutes.");
        }

        if (options.SweepIntervalMinutes < 1)
        {
            failures.Add("Extraction:SweepIntervalMinutes must be at least one.");
        }

        return OptionValidation.Result(failures);
    }
}

public sealed class AzureSignalROptionsValidator : IValidateOptions<AzureSignalROptions>
{
    public ValidateOptionsResult Validate(string? name, AzureSignalROptions options)
    {
        var failures = new List<string>();
        OptionValidation.RequireHttpsUri(failures, "AzureSignalR:Endpoint", options.Endpoint, requireRoot: true);
        if (!string.Equals(options.HubName, "demands", StringComparison.Ordinal))
        {
            failures.Add("AzureSignalR:HubName must be demands.");
        }

        return OptionValidation.Result(failures);
    }
}