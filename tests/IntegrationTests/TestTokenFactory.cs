using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace IntegrationTests;

public sealed class TestTokenFactory : IDisposable
{
    public const string Issuer = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0";
    public const string Audience = "api://22222222-2222-2222-2222-222222222222";
    public const string TenantId = "11111111-1111-1111-1111-111111111111";
    public const string Oid = "33333333-3333-3333-3333-333333333333";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RSA _otherRsa = RSA.Create(2048);

    public string Create(TestTokenKind kind = TestTokenKind.Valid)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new("tid", kind == TestTokenKind.WrongTenant ? "44444444-4444-4444-4444-444444444444" : TenantId),
            new("oid", kind == TestTokenKind.MissingOid ? string.Empty : Oid),
            new("nonce", "test-nonce"),
            new("ver", "2.0")
        };

        if (kind != TestTokenKind.MissingScope)
        {
            claims.Add(new Claim("scp", "access_as_user"));
        }

        var signingCredentials = kind switch
        {
            TestTokenKind.WrongSignature => new SigningCredentials(
                new RsaSecurityKey(_otherRsa), SecurityAlgorithms.RsaSha256),
            TestTokenKind.Unsigned => null,
            _ => new SigningCredentials(new RsaSecurityKey(_rsa), SecurityAlgorithms.RsaSha256)
        };

        var issuer = kind == TestTokenKind.WrongIssuer ? "https://invalid.example/" : Issuer;
        var audience = kind == TestTokenKind.WrongAudience ? "api://invalid" : Audience;
        var expires = kind == TestTokenKind.Expired ? now.AddMinutes(-1) : now.AddHours(1);
        var notBefore = kind == TestTokenKind.Expired ? now.AddHours(-2) : now.AddMinutes(-1);

        var securityToken = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            notBefore,
            expires,
            signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(securityToken);
    }

    public RSAParameters SigningKeyParameters => _rsa.ExportParameters(false);

    public void Dispose()
    {
        _rsa.Dispose();
        _otherRsa.Dispose();
    }
}

public enum TestTokenKind
{
    Valid,
    WrongIssuer,
    WrongAudience,
    WrongTenant,
    MissingScope,
    MissingOid,
    Expired,
    WrongSignature,
    Unsigned
}
