using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using IntegrationTests.Contract;
using Klesia.Application.Abstractions;
using Klesia.Domain.Demands;
using Klesia.Domain.Users;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace IntegrationTests;

public sealed class ListDemandsTests
{
    private static readonly Guid OwnerOid = Guid.Parse(TestTokenFactory.Oid);
    private static readonly Guid OtherOid = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private const int OwnerUserId = 101;
    private const int OtherUserId = 202;

    [Fact]
    public async Task At01_NoBearerToken_Returns401Problem()
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/demands");

        await AssertProblemResponseAsync(response, HttpStatusCode.Unauthorized);
        ContractValidator.AssertResponse(response, "listDemands");
    }

    [Theory]
    [InlineData(TestTokenKind.Expired)]
    [InlineData(TestTokenKind.WrongSignature)]
    [InlineData(TestTokenKind.Unsigned)]
    [InlineData(TestTokenKind.WrongAudience)]
    [InlineData(TestTokenKind.WrongTenant)]
    [InlineData(TestTokenKind.IdToken)]
    [InlineData(TestTokenKind.MissingOid)]
    public async Task At02At03At05At20_InvalidToken_Returns401Problem(TestTokenKind kind)
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();
        using var request = RequestWithToken(factory.Tokens.Create(kind));

        using var response = await client.SendAsync(request);

        await AssertProblemResponseAsync(response, HttpStatusCode.Unauthorized);
        ContractValidator.AssertResponse(response, "listDemands");
    }

    [Fact]
    public async Task At04At06At07_MissingScopeUnknownAndInactiveUsersReturnIdentical403()
    {
        using var factory = new KlesiaApiFactory();
        var users = Substitute.For<IUserRepository>();
        var demands = Substitute.For<IDemandRepository>();
        users.GetByUniqueIdAsync(OwnerOid, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ApplicationUser?>(new ApplicationUser(OwnerUserId, OwnerOid, "Example User", true)));
        users.GetByUniqueIdAsync(OtherOid, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ApplicationUser?>(new ApplicationUser(OtherUserId, OtherOid, "Inactive User", false)));
        using var host = CreateHost(factory, users, demands);
        using var client = host.CreateClient();

        using var missingScope = await client.SendAsync(RequestWithToken(
            factory.Tokens.Create(TestTokenKind.MissingScope, OwnerOid.ToString("D"))));
        using var unknownUser = await client.SendAsync(RequestWithToken(
            factory.Tokens.Create(oidOverride: "55555555-5555-5555-5555-555555555555")));
        using var inactiveUser = await client.SendAsync(RequestWithToken(
            factory.Tokens.Create(oidOverride: OtherOid.ToString("D"))));

        await AssertProblemResponseAsync(missingScope, HttpStatusCode.Forbidden);
        await AssertProblemResponseAsync(unknownUser, HttpStatusCode.Forbidden);
        await AssertProblemResponseAsync(inactiveUser, HttpStatusCode.Forbidden);
        ContractValidator.AssertResponse(missingScope, "listDemands");
        ContractValidator.AssertResponse(unknownUser, "listDemands");
        ContractValidator.AssertResponse(inactiveUser, "listDemands");
        (await ProblemWithoutTraceIdAsync(missingScope)).Should().Be(await ProblemWithoutTraceIdAsync(unknownUser));
        (await ProblemWithoutTraceIdAsync(unknownUser)).Should().Be(await ProblemWithoutTraceIdAsync(inactiveUser));
        await demands.DidNotReceive().ListByUserAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task At08_ActiveUserWithoutDemands_ReturnsExactEmptyResponse()
    {
        using var factory = new KlesiaApiFactory();
        var users = Substitute.For<IUserRepository>();
        var demands = Substitute.For<IDemandRepository>();
        ConfigureActiveUser(users, OwnerOid, OwnerUserId, "Example User");
        demands.ListByUserAsync(OwnerUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Demand>>([]));
        using var host = CreateHost(factory, users, demands);
        using var client = host.CreateClient();

        using var response = await client.SendAsync(RequestWithToken(factory.Tokens.Create()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        ContractValidator.AssertResponse(response, "listDemands");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("user_id", "user_name", "demands");
        root.GetProperty("user_id").GetString().Should().Be(OwnerOid.ToString("D"));
        root.GetProperty("user_name").GetString().Should().Be("Example User");
        root.GetProperty("demands").GetArrayLength().Should().Be(0);
        await demands.Received(1).ListByUserAsync(OwnerUserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task At09At55_MultipleDemandsMatchContractAndNeverExposeStoredBlobUrls()
    {
        const string firstBlobUrl = "https://contoso.blob.core.windows.net/bae/first.pdf";
        const string secondBlobUrl = "https://contoso.blob.core.windows.net/bae/second.pdf";
        const string firstGkNumber = "GK260902-144743112";
        const string secondGkNumber = "GK260902-144743113";
        using var factory = new KlesiaApiFactory();
        var users = Substitute.For<IUserRepository>();
        var demands = Substitute.For<IDemandRepository>();
        ConfigureActiveUser(users, OwnerOid, OwnerUserId, "Example User");
        demands.ListByUserAsync(OwnerUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Demand>>(
            new Demand[]
            {
                new Demand(12, OwnerUserId, GKNumber.Create(firstGkNumber), DemandStatus.Fail),
                new Demand(11, OwnerUserId, GKNumber.Create(secondGkNumber), DemandStatus.Ok)
            }));
        using var host = CreateHost(factory, users, demands);
        using var client = host.CreateClient();
        factory.LogSink.Clear();

        using var response = await client.SendAsync(RequestWithToken(factory.Tokens.Create()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ContractValidator.AssertResponse(response, "listDemands");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(firstBlobUrl).And.NotContain(secondBlobUrl);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("user_id", "user_name", "demands");
        var items = root.GetProperty("demands").EnumerateArray().ToArray();
        items.Should().HaveCount(2);
        items.Select(item => item.EnumerateObject().Select(property => property.Name).ToArray())
            .Should().OnlyContain(names => names.ToHashSet(StringComparer.Ordinal)
                .SetEquals(new[] { "demand_id", "gk_number", "bae_link", "status" }));
        items[0].GetProperty("demand_id").GetInt32().Should().Be(12);
        items[0].GetProperty("gk_number").GetString().Should().Be(firstGkNumber);
        items[0].GetProperty("bae_link").GetString().Should().Be("/api/v1/demands/12/bae");
        items[0].GetProperty("status").GetString().Should().Be("fail");
        items[1].GetProperty("demand_id").GetInt32().Should().Be(11);
        items[1].GetProperty("gk_number").GetString().Should().Be(secondGkNumber);
        items[1].GetProperty("bae_link").GetString().Should().Be("/api/v1/demands/11/bae");
        items[1].GetProperty("status").GetString().Should().Be("ok");
        var logMessages = string.Join(Environment.NewLine, factory.LogSink.Entries.Select(entry => entry.Message));
        logMessages.Should().NotContain(firstGkNumber)
            .And.NotContain(secondGkNumber)
            .And.NotContain("Example User");
    }

    [Fact]
    public async Task At10At11_TwoUsersAreIsolatedAndUserIdInputsAreIgnored()
    {
        using var factory = new KlesiaApiFactory();
        var users = Substitute.For<IUserRepository>();
        var demands = Substitute.For<IDemandRepository>();
        ConfigureActiveUser(users, OwnerOid, OwnerUserId, "Owner User");
        ConfigureActiveUser(users, OtherOid, OtherUserId, "Other User");
        demands.ListByUserAsync(OwnerUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Demand>>(
                [new Demand(31, OwnerUserId, GKNumber.Create("GK260902-144743131"), DemandStatus.Pending)]));
        demands.ListByUserAsync(OtherUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Demand>>(
                [new Demand(41, OtherUserId, GKNumber.Create("GK260902-144743141"), DemandStatus.Pending)]));
        using var host = CreateHost(factory, users, demands);
        using var client = host.CreateClient();

        using var ownerRequest = RequestWithToken(factory.Tokens.Create(oidOverride: OwnerOid.ToString("D")));
        ownerRequest.RequestUri = new Uri("/api/v1/demands?user_id=" + OtherOid.ToString("D"), UriKind.Relative);
        ownerRequest.Headers.TryAddWithoutValidation("user_id", OtherOid.ToString("D"));
        using var ownerResponse = await client.SendAsync(ownerRequest);
        using var otherResponse = await client.SendAsync(RequestWithToken(
            factory.Tokens.Create(oidOverride: OtherOid.ToString("D"))));

        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        otherResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        ContractValidator.AssertResponse(ownerResponse, "listDemands");
        ContractValidator.AssertResponse(otherResponse, "listDemands");
        using var ownerDocument = JsonDocument.Parse(await ownerResponse.Content.ReadAsStringAsync());
        using var otherDocument = JsonDocument.Parse(await otherResponse.Content.ReadAsStringAsync());
        ownerDocument.RootElement.GetProperty("demands")[0].GetProperty("gk_number").GetString()
            .Should().Be("GK260902-144743131");
        otherDocument.RootElement.GetProperty("demands")[0].GetProperty("gk_number").GetString()
            .Should().Be("GK260902-144743141");
        await demands.Received(1).ListByUserAsync(OwnerUserId, Arg.Any<CancellationToken>());
        await demands.Received(1).ListByUserAsync(OtherUserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task At12At21_RepositoryFailureReturnsSanitizedCorrelated500Problem()
    {
        using var factory = new KlesiaApiFactory();
        var users = Substitute.For<IUserRepository>();
        var demands = Substitute.For<IDemandRepository>();
        ConfigureActiveUser(users, OwnerOid, OwnerUserId, "Example User");
        demands.ListByUserAsync(OwnerUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<Demand>>(
                new InvalidOperationException("SENSITIVE_REPOSITORY_FAILURE")));
        using var host = CreateHost(factory, users, demands);
        using var client = host.CreateClient();

        using var response = await client.SendAsync(RequestWithToken(factory.Tokens.Create()));

        await AssertProblemResponseAsync(response, HttpStatusCode.InternalServerError);
        ContractValidator.AssertResponse(response, "listDemands");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("SENSITIVE_REPOSITORY_FAILURE");
    }

    private static WebApplicationFactory<Program> CreateHost(
        KlesiaApiFactory factory,
        IUserRepository users,
        IDemandRepository demands)
    {
        return factory.WithServices(services =>
        {
            services.RemoveAll<IUserRepository>();
            services.RemoveAll<IDemandRepository>();
            services.AddSingleton(users);
            services.AddSingleton(demands);
        });
    }

    private static void ConfigureActiveUser(IUserRepository users, Guid oid, int userId, string name)
    {
        users.GetByUniqueIdAsync(oid, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ApplicationUser?>(new ApplicationUser(userId, oid, name, true)));
    }

    private static HttpRequestMessage RequestWithToken(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/demands");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task AssertProblemResponseAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        response.StatusCode.Should().Be(expectedStatus);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var correlationId = response.Headers.GetValues("X-Correlation-Id").Single();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("status").GetInt32().Should().Be((int)expectedStatus);
        document.RootElement.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        document.RootElement.GetProperty("traceId").GetString().Should().Be(correlationId);
    }

    private static async Task<string> ProblemWithoutTraceIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return $"{document.RootElement.GetProperty("title").GetString()}|{document.RootElement.GetProperty("status").GetInt32()}";
    }
}