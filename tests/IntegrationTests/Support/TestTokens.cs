using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace IntegrationTests.Support;

/// <summary>Mints real RS256 JWTs signed with a test key so the production validation pipeline is exercised.</summary>
public static class TestTokens
{
    public const string RequiredScope = "access_as_user";

    public static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid ClientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly RsaSecurityKey TrustedKey = CreateKey();
    private static readonly RsaSecurityKey ForgedKey = CreateKey();

    public static string IssuerOf(Guid tenantId) => $"https://login.microsoftonline.com/{tenantId}/v2.0";

    /// <summary>Replaces the metadata download: the API trusts only the test signing key.</summary>
    public static OpenIdConnectConfiguration BuildConfiguration()
    {
        OpenIdConnectConfiguration configuration = new() { Issuer = IssuerOf(TenantId) };
        configuration.SigningKeys.Add(TrustedKey);
        return configuration;
    }

    public static string Create(TokenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        DateTime now = DateTime.UtcNow;
        DateTime expires = now.Add(options.Lifetime);
        DateTime notBefore = (expires < now ? expires : now).AddMinutes(-5);

        Dictionary<string, object> claims = new() { ["tid"] = options.TenantId.ToString() };

        if (options.ObjectId is not null)
        {
            claims["oid"] = options.ObjectId;
        }

        if (options.Scope is not null)
        {
            claims["scp"] = options.Scope;
        }

        if (options.Nonce is not null)
        {
            claims["nonce"] = options.Nonce;
        }

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            NotBefore = notBefore,
            Expires = expires,
            Claims = claims,
            SigningCredentials = new SigningCredentials(options.Forged ? ForgedKey : TrustedKey, SecurityAlgorithms.RsaSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static RsaSecurityKey CreateKey() => new(RSA.Create(2048)) { KeyId = "test-key" };
}

public sealed class TokenOptions
{
    /// <summary>The <c>oid</c> claim; <see langword="null"/> omits the claim.</summary>
    public string? ObjectId { get; set; }

    public Guid TenantId { get; set; } = TestTokens.TenantId;

    public string Issuer { get; set; } = TestTokens.IssuerOf(TestTokens.TenantId);

    public string Audience { get; set; } = TestTokens.ClientId.ToString();

    /// <summary>The <c>scp</c> claim; <see langword="null"/> simulates an app-only token.</summary>
    public string? Scope { get; set; } = TestTokens.RequiredScope;

    /// <summary>A <c>nonce</c> claim is present in ID tokens only.</summary>
    public string? Nonce { get; set; }

    /// <summary>A negative value produces an expired token.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Signs with a key the API does not trust.</summary>
    public bool Forged { get; set; }
}
