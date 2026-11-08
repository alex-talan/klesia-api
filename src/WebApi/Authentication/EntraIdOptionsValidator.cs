using Microsoft.Extensions.Options;

namespace WebApi.Authentication;

public sealed class EntraIdOptionsValidator : IValidateOptions<EntraIdOptions>
{
    public ValidateOptionsResult Validate(string? name, EntraIdOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (!Uri.TryCreate(options.Instance, UriKind.Absolute, out Uri? instance) || instance.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add("AzureAd:Instance must be an absolute HTTPS URL.");
        }

        if (!Guid.TryParse(options.TenantId, out _))
        {
            failures.Add("AzureAd:TenantId must be the tenant GUID (replace the placeholder).");
        }

        if (!Guid.TryParse(options.ClientId, out _))
        {
            failures.Add("AzureAd:ClientId must be the API application (client) ID GUID (replace the placeholder).");
        }

        if (string.IsNullOrWhiteSpace(options.RequiredScope)
            || options.RequiredScope.Any(character => char.IsWhiteSpace(character) || character is '<' or '>'))
        {
            failures.Add("AzureAd:RequiredScope must be a single scope name such as 'access_as_user' (replace the placeholder).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
