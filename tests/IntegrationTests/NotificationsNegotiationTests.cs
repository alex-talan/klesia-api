using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using IntegrationTests.Contract;
using Klesia.Application.Abstractions;
using Klesia.Application.Notifications;
using Klesia.Domain.Users;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace IntegrationTests;

public sealed class NotificationsNegotiationTests
{
    private const string Route = "/api/v1/notifications/negotiate";
    private const int OwnerUserId = 101;
    private const int InactiveUserId = 202;
    private static readonly Guid OwnerOid = Guid.Parse(TestTokenFactory.Oid);
    private static readonly Guid InactiveOid = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid UnknownOid = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Uri ClientEndpoint = new("https://klesia-dev.service.signalr.net/client/?hub=demands");
    private const string ClientAccessToken = "test-signalr-connection-token";

    [Fact]
    public async Task At31_ActiveUser_ReturnsRedirectPayloadAndUsesValidatedOid()
    {
        using var factory = new KlesiaApiFactory();
        var users = Substitute.For<IUserRepository>();
        var negotiator = Substitute.For<IRealtimeNegotiator>();
        ConfigureActiveUser(users, OwnerOid, OwnerUserId);
        negotiator.NegotiateAsync(OwnerOid, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RealtimeNegotiation(ClientEndpoint, ClientAccessToken)));
        using var host = CreateHost(factory, users, negotiator);
        using var client = host.CreateClient();
        var requestToken = factory.Tokens.Create();
        using var request = RequestWithToken(requestToken, "?negotiateVersion=1");
        request.Content = new ByteArrayContent([]);
        factory.LogSink.Clear();

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        ContractValidator.AssertResponse(response, "negotiateNotifications");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("url", "accessToken");
        root.GetProperty("url").GetString().Should().Be(ClientEndpoint.ToString());
        root.GetProperty("accessToken").GetString().Should().Be(ClientAccessToken);
        (await LogsAsync(factory)).Should().NotContain(requestToken).And.NotContain(ClientAccessToken);
        await negotiator.Received(1).NegotiateAsync(OwnerOid, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task At18_UnauthenticatedMissingScopeUnknownAndInactiveUsersCannotNegotiate()
    {
        using var factory = new KlesiaApiFactory();
        var users = Substitute.For<IUserRepository>();
        var negotiator = Substitute.For<IRealtimeNegotiator>();
        ConfigureActiveUser(users, OwnerOid, OwnerUserId);
        users.GetByUniqueIdAsync(InactiveOid, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ApplicationUser?>(
                new ApplicationUser(InactiveUserId, InactiveOid, "Inactive User", false)));
        using var host = CreateHost(factory, users, negotiator);
        using var client = host.CreateClient();

        using var missingToken = await client.SendAsync(RequestWithToken(null));
        using var missingScope = await client.SendAsync(RequestWithToken(
            factory.Tokens.Create(TestTokenKind.MissingScope, OwnerOid.ToString("D"))));
        using var unknownUser = await client.SendAsync(RequestWithToken(
            factory.Tokens.Create(oidOverride: UnknownOid.ToString("D"))));
        using var inactiveUser = await client.SendAsync(RequestWithToken(
            factory.Tokens.Create(oidOverride: InactiveOid.ToString("D"))));

        await AssertProblemResponseAsync(missingToken, HttpStatusCode.Unauthorized);
        await AssertProblemResponseAsync(missingScope, HttpStatusCode.Forbidden);
        await AssertProblemResponseAsync(unknownUser, HttpStatusCode.Forbidden);
        await AssertProblemResponseAsync(inactiveUser, HttpStatusCode.Forbidden);
        ContractValidator.AssertResponse(missingToken, "negotiateNotifications");
        ContractValidator.AssertResponse(missingScope, "negotiateNotifications");
        ContractValidator.AssertResponse(unknownUser, "negotiateNotifications");
        ContractValidator.AssertResponse(inactiveUser, "negotiateNotifications");
        (await ProblemWithoutTraceIdAsync(unknownUser)).Should().Be(await ProblemWithoutTraceIdAsync(inactiveUser));
        await negotiator.DidNotReceive().NegotiateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignalRUnavailable_ReturnsSanitized503WithoutLoggingConnectionDetails()
    {
        using var factory = new KlesiaApiFactory();
        var users = Substitute.For<IUserRepository>();
        var negotiator = Substitute.For<IRealtimeNegotiator>();
        ConfigureActiveUser(users, OwnerOid, OwnerUserId);
        negotiator.NegotiateAsync(OwnerOid, Arg.Any<CancellationToken>())
            .Returns<Task<RealtimeNegotiation>>(_ => throw new RealtimeNegotiationUnavailableException());
        using var host = CreateHost(factory, users, negotiator);
        using var client = host.CreateClient();
        factory.LogSink.Clear();

        using var response = await client.SendAsync(RequestWithToken(factory.Tokens.Create()));

        await AssertProblemResponseAsync(response, HttpStatusCode.ServiceUnavailable);
        ContractValidator.AssertResponse(response, "negotiateNotifications");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(ClientAccessToken).And.NotContain(ClientEndpoint.ToString());
        var logs = await LogsAsync(factory);
        logs.Should().Contain("Azure SignalR negotiation unavailable")
            .And.NotContain(ClientAccessToken)
            .And.NotContain(ClientEndpoint.ToString());
    }

    private static WebApplicationFactory<Program> CreateHost(
        KlesiaApiFactory factory,
        IUserRepository users,
        IRealtimeNegotiator negotiator) =>
        factory.WithServices(services =>
        {
            services.RemoveAll<IUserRepository>();
            services.RemoveAll<IRealtimeNegotiator>();
            services.AddSingleton(users);
            services.AddSingleton(negotiator);
        });

    private static void ConfigureActiveUser(IUserRepository users, Guid oid, int userId)
    {
        users.GetByUniqueIdAsync(oid, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ApplicationUser?>(new ApplicationUser(userId, oid, "Example User", true)));
    }

    private static HttpRequestMessage RequestWithToken(string? token, string query = "")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Route + query);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private static async Task AssertProblemResponseAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var correlationId = response.Headers.GetValues("X-Correlation-Id").Single();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("status").GetInt32().Should().Be((int)status);
        document.RootElement.GetProperty("traceId").GetString().Should().Be(correlationId);
    }

    private static async Task<string> ProblemWithoutTraceIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return $"{document.RootElement.GetProperty("title").GetString()}|{document.RootElement.GetProperty("status").GetInt32()}";
    }

    private static Task<string> LogsAsync(KlesiaApiFactory factory) =>
        Task.FromResult(string.Join(Environment.NewLine, factory.LogSink.Entries.Select(entry => entry.Message)));
}