using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Domain.Demands;
using Domain.Users;
using FluentAssertions;
using IntegrationTests.Support;

namespace IntegrationTests.Api;

/// <summary>Sprint 1 acceptance scenarios AT-01 to AT-12 (plus identity-claim edge cases) for GET /api/v1/demands.</summary>
public sealed class DemandsEndpointTests : IDisposable
{
    private const string Route = "/api/v1/demands";

    private readonly KlesiaWebApplicationFactory factory = new();
    private readonly Guid aliceId = Guid.NewGuid();
    private readonly Guid bobId = Guid.NewGuid();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task At01_MissingBearerToken_Returns401()
    {
        using HttpResponseMessage response = await GetAsync(token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task At01_MalformedAuthorizationHeader_Returns401()
    {
        using HttpResponseMessage response = await GetAsync(token: "not-a-jwt");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task At02_ExpiredToken_Returns401()
    {
        AddActiveUser(aliceId, 1, "Alice");

        using HttpResponseMessage response = await GetAsync(Token(aliceId, o => o.Lifetime = TimeSpan.FromHours(-1)));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task At02_TokenSignedWithUntrustedKey_Returns401()
    {
        AddActiveUser(aliceId, 1, "Alice");

        using HttpResponseMessage response = await GetAsync(Token(aliceId, o => o.Forged = true));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task At02_WrongAudience_Returns401()
    {
        AddActiveUser(aliceId, 1, "Alice");

        using HttpResponseMessage response = await GetAsync(Token(aliceId, o => o.Audience = Guid.NewGuid().ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task At03_MicrosoftGraphToken_Returns401()
    {
        AddActiveUser(aliceId, 1, "Alice");

        using HttpResponseMessage response = await GetAsync(Token(aliceId, o => o.Audience = "00000003-0000-0000-c000-000000000000"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task At03_IdToken_Returns401()
    {
        AddActiveUser(aliceId, 1, "Alice");

        using HttpResponseMessage response = await GetAsync(Token(aliceId, o =>
        {
            o.Nonce = "n-0S6_WzA2Mj";
            o.Scope = null;
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task At04_MissingRequiredScope_Returns403()
    {
        AddActiveUser(aliceId, 1, "Alice");

        using HttpResponseMessage response = await GetAsync(Token(aliceId, o => o.Scope = "some.other.scope"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task At04_ApplicationOnlyTokenWithoutScp_Returns403()
    {
        AddActiveUser(aliceId, 1, "Alice");

        using HttpResponseMessage response = await GetAsync(Token(aliceId, o => o.Scope = null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task At04_RequiredScopeAmongSeveral_IsAccepted()
    {
        AddActiveUser(aliceId, 1, "Alice");

        using HttpResponseMessage response = await GetAsync(Token(aliceId, o => o.Scope = $"profile {TestTokens.RequiredScope}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task At05_TokenIssuedByAnotherTenant_Returns401()
    {
        AddActiveUser(aliceId, 1, "Alice");
        Guid otherTenant = Guid.NewGuid();

        using HttpResponseMessage response = await GetAsync(Token(aliceId, o =>
        {
            o.TenantId = otherTenant;
            o.Issuer = TestTokens.IssuerOf(otherTenant);
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task At05_TidClaimOfAnotherTenantUnderTrustedIssuer_Returns401()
    {
        AddActiveUser(aliceId, 1, "Alice");

        using HttpResponseMessage response = await GetAsync(Token(aliceId, o => o.TenantId = Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ValidTokenWithoutOidClaim_Returns401()
    {
        using HttpResponseMessage response = await GetAsync(Token(objectId: null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task At06_UserNotInAllowlist_Returns403()
    {
        using HttpResponseMessage response = await GetAsync(Token(aliceId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task At07_InactiveUser_Returns403_WithSameProblemAsUnknownUser()
    {
        factory.Users.Add(new ApplicationUser(1, aliceId, "Alice", IsActive: false));

        using HttpResponseMessage inactive = await GetAsync(Token(aliceId));
        using HttpResponseMessage unknown = await GetAsync(Token(Guid.NewGuid()));

        inactive.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        unknown.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemTitleAsync(inactive)).Should().Be(await ProblemTitleAsync(unknown));
    }

    [Fact]
    public async Task At08_ActiveUserWithoutDemands_Returns200WithEmptyList()
    {
        AddActiveUser(aliceId, 1, "Alice");

        using HttpResponseMessage response = await GetAsync(Token(aliceId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        using JsonDocument json = await ReadJsonAsync(response);
        json.RootElement.GetProperty("user_id").GetString().Should().Be(aliceId.ToString());
        json.RootElement.GetProperty("user_name").GetString().Should().Be("Alice");
        json.RootElement.GetProperty("demands").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task At09_ActiveUserWithDemands_ReturnsExactSnakeCaseContract()
    {
        AddActiveUser(aliceId, 1, "John DOE");
        AddDemand(1, 10, "732829320", "https://contoso.sharepoint.com/sites/klesia/bae-10.pdf", "pending");
        AddDemand(1, 11, "007000000", "https://contoso.sharepoint.com/sites/klesia/bae-11.pdf", "ok");

        using HttpResponseMessage response = await GetAsync(Token(aliceId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument json = await ReadJsonAsync(response);
        json.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("user_id", "user_name", "demands");

        JsonElement[] demands = json.RootElement.GetProperty("demands").EnumerateArray().ToArray();
        demands.Select(demand => demand.GetProperty("demand_id").GetInt32()).Should().Equal(11, 10);
        demands[0].EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("demand_id", "siren_number", "bae_link", "status");
        demands[0].GetProperty("siren_number").GetString().Should().Be("007-000-000");
        demands[0].GetProperty("status").GetString().Should().Be("ok");
        demands[1].GetProperty("siren_number").GetString().Should().Be("732-829-320");
        demands[1].GetProperty("bae_link").GetString().Should().StartWith("https://");
        demands[1].GetProperty("status").GetString().Should().Be("pending");
    }

    [Fact]
    public async Task At10_EachUserOnlyReceivesOwnDemands()
    {
        AddActiveUser(aliceId, 1, "Alice");
        AddActiveUser(bobId, 2, "Bob");
        AddDemand(1, 10, "111111111", "https://contoso.sharepoint.com/a.pdf", "pending");
        AddDemand(2, 20, "222222222", "https://contoso.sharepoint.com/b.pdf", "fail");

        using HttpResponseMessage aliceResponse = await GetAsync(Token(aliceId));
        using HttpResponseMessage bobResponse = await GetAsync(Token(bobId));

        (await DemandIdsAsync(aliceResponse)).Should().Equal(10);
        (await DemandIdsAsync(bobResponse)).Should().Equal(20);
    }

    [Fact]
    public async Task At11_UserIdInQueryOrHeader_IsIgnored()
    {
        AddActiveUser(aliceId, 1, "Alice");
        AddActiveUser(bobId, 2, "Bob");
        AddDemand(1, 10, "111111111", "https://contoso.sharepoint.com/a.pdf", "pending");
        AddDemand(2, 20, "222222222", "https://contoso.sharepoint.com/b.pdf", "fail");

        using HttpResponseMessage response = await GetAsync(
            Token(aliceId),
            route: $"{Route}?user_id={bobId}&userId=2",
            extraHeaderName: "X-User-Id",
            extraHeaderValue: bobId.ToString());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument json = await ReadJsonAsync(response);
        json.RootElement.GetProperty("user_id").GetString().Should().Be(aliceId.ToString());
        json.RootElement.GetProperty("demands").EnumerateArray()
            .Select(demand => demand.GetProperty("demand_id").GetInt32()).Should().Equal(10);
    }

    [Fact]
    public async Task At12_DatabaseFailure_Returns500WithoutLeakingDetails()
    {
        factory.Users.Failure = new InvalidOperationException("Server=tcp:secret.database.windows.net;Password=hunter2");

        using HttpResponseMessage response = await GetAsync(Token(aliceId));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("hunter2").And.NotContain("secret.database").And.NotContain("InvalidOperationException");
    }

    [Fact]
    public async Task At19_StoredLinkThatIsNotHttps_Returns500WithoutLeakingTheLink()
    {
        AddActiveUser(aliceId, 1, "Alice");
        AddDemand(1, 10, "732829320", "ftp://internal.example.com/secret.pdf", "pending");

        using HttpResponseMessage response = await GetAsync(Token(aliceId));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("internal.example.com");
    }

    [Fact]
    public async Task Health_IsAnonymous()
    {
        using HttpResponseMessage response = await GetAsync(token: null, route: "/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private void AddActiveUser(Guid userUniqueId, int userId, string name) =>
        factory.Users.Add(new ApplicationUser(userId, userUniqueId, name, IsActive: true));

    private void AddDemand(int userId, int demandId, string siren, string link, string status) =>
        factory.Demands.Add(userId, new Demand(demandId, SirenNumber.Create(siren), link, status));

    private static string Token(Guid objectId, Action<TokenOptions>? configure = null) => Token(objectId.ToString(), configure);

    private static string Token(string? objectId, Action<TokenOptions>? configure = null)
    {
        TokenOptions options = new() { ObjectId = objectId };
        configure?.Invoke(options);
        return TestTokens.Create(options);
    }

    private async Task<HttpResponseMessage> GetAsync(
        string? token,
        string route = Route,
        string? extraHeaderName = null,
        string? extraHeaderValue = null)
    {
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, route);
        request.Headers.Accept.ParseAdd("application/json");

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (extraHeaderName is not null)
        {
            request.Headers.Add(extraHeaderName, extraHeaderValue);
        }

        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static async Task<int[]> DemandIdsAsync(HttpResponseMessage response)
    {
        using JsonDocument json = await ReadJsonAsync(response);
        return json.RootElement.GetProperty("demands").EnumerateArray()
            .Select(demand => demand.GetProperty("demand_id").GetInt32())
            .ToArray();
    }

    private static async Task<string?> ProblemTitleAsync(HttpResponseMessage response)
    {
        using JsonDocument json = await ReadJsonAsync(response);
        return json.RootElement.GetProperty("title").GetString();
    }
}
