using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using IntegrationTests.Contract;
using IntegrationTests.Fakes;
using Klesia.Application.Abstractions;
using Klesia.Domain.Demands;
using Klesia.Domain.Users;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace IntegrationTests;

public sealed class CreateDemandEndpointTests
{
    private static readonly Guid OwnerOid = Guid.Parse(TestTokenFactory.Oid);
    private const int OwnerUserId = 401;
    private const string ValidGkNumber = "GK260902-144743112";
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\ncontent");
    private const string Filename = "private-name.pdf";

    [Fact]
    public void ApiHostFormOptionsLimitTracksPdfLimitPlusMultipartOverhead()
    {
        using var factory = new KlesiaApiFactory();

        var options = factory.Services.GetRequiredService<IOptions<FormOptions>>().Value;

        options.MultipartBodyLengthLimit.Should().Be(10L * 1_048_576 + 64 * 1024);
    }

    [Fact]
    public async Task At23At39_ValidSubmissionReturnsCreatedThenAppearsInListAfterN1()
    {
        using var factory = new KlesiaApiFactory();
        var users = CreateUserRepository(new ApplicationUser(OwnerUserId, OwnerOid, "Example User", true));
        var demands = new DemandRepository();
        var store = CreateDocumentStore();
        var events = new List<string>();
        demands.OnCreated = () => events.Add("commit");
        var notifier = Substitute.For<INotifier>();
        notifier.NotifyDemandsChangedAsync(OwnerOid, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                events.Add("notify");
                return Task.CompletedTask;
            });
        var queue = Substitute.For<IExtractionQueue>();
        var extractionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var extractionGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.TryEnqueue(Arg.Any<int>()).Returns(call =>
        {
            events.Add("enqueue");
            _ = Task.Run(async () =>
            {
                extractionStarted.SetResult();
                await extractionGate.Task;
            });
            return true;
        });
        using var host = CreateHost(factory, users, demands, store, notifier, queue);
        using var client = host.CreateClient();
        factory.LogSink.Clear();

        using var response = await client.SendAsync(CreateRequest(factory, ValidGkNumber, PdfBytes));

        var responseBody = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Response: {responseBody}");
        response.Headers.Location.Should().BeNull();
        ContractValidator.AssertResponse(response, "createDemand");
        using var body = JsonDocument.Parse(responseBody);
        body.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("demand_id", "gk_number", "bae_link", "status");
        var demandId = body.RootElement.GetProperty("demand_id").GetInt32();
        body.RootElement.GetProperty("gk_number").GetString().Should().Be(ValidGkNumber);
        body.RootElement.GetProperty("bae_link").GetString().Should().Be($"/api/v1/demands/{demandId}/bae");
        body.RootElement.GetProperty("status").GetString().Should().Be("pending");
        await extractionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        extractionGate.Task.IsCompleted.Should().BeFalse();
        events.Should().Equal("commit", "notify", "enqueue");
        await notifier.Received(1).NotifyDemandsChangedAsync(OwnerOid, Arg.Any<CancellationToken>());
        queue.Received(1).TryEnqueue(demandId);

        using var listResponse = await client.SendAsync(RequestWithToken(factory, HttpMethod.Get, "/api/v1/demands"));
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        ContractValidator.AssertResponse(listResponse, "listDemands");
        using var listBody = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var listedDemand = listBody.RootElement.GetProperty("demands")[0];
        listedDemand.GetProperty("demand_id").GetInt32().Should().Be(demandId);
        listedDemand.GetProperty("bae_link").GetString().Should().Be($"/api/v1/demands/{demandId}/bae");
        var logs = string.Join(Environment.NewLine, factory.LogSink.Entries.Select(entry => entry.Message));
        logs.Should().NotContain(ValidGkNumber).And.NotContain(Filename);
        logs.Should().NotContain(store.UploadedBlobUrls.Single().ToString());
        extractionGate.SetResult();
    }

    [Theory]
    [InlineData("GK260902144743112")]
    [InlineData("gk260902-144743112")]
    [InlineData(" GK260902-144743112")]
    public async Task At24_InvalidGkReturns400WithoutUploading(string gkNumber)
    {
        using var factory = new KlesiaApiFactory();
        var users = CreateUserRepository(new ApplicationUser(OwnerUserId, OwnerOid, "Example User", true));
        var demands = new DemandRepository();
        var store = CreateDocumentStore();
        using var host = CreateHost(factory, users, demands, store);
        using var client = host.CreateClient();

        using var response = await client.SendAsync(CreateRequest(factory, gkNumber, PdfBytes));

        await AssertProblemAsync(response, factory, HttpStatusCode.BadRequest, "createDemand");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("errors").TryGetProperty("gk_number", out _).Should().BeTrue();
        store.DocumentCount.Should().Be(0);
        demands.Demands.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing-gk")]
    [InlineData("missing-file")]
    [InlineData("duplicate-gk")]
    [InlineData("duplicate-file")]
    [InlineData("user-id")]
    public async Task At25At50_InvalidPartShapeReturns400WithoutStorage(string scenario)
    {
        using var factory = new KlesiaApiFactory();
        var users = CreateUserRepository(new ApplicationUser(OwnerUserId, OwnerOid, "Example User", true));
        var demands = new DemandRepository();
        var store = CreateDocumentStore();
        using var host = CreateHost(factory, users, demands, store);
        using var client = host.CreateClient();
        using var multipart = CreateMultipart(scenario == "missing-gk" ? null : ValidGkNumber);
        if (scenario != "missing-file")
        {
            AddPdf(multipart, PdfBytes);
        }

        if (scenario == "duplicate-file")
        {
            AddPdf(multipart, PdfBytes, "second.pdf");
        }

        if (scenario == "duplicate-gk")
        {
            multipart.Add(new StringContent(ValidGkNumber), "gk_number");
        }

        if (scenario == "user-id")
        {
            multipart.Add(new StringContent(OwnerOid.ToString("D")), "user_id");
        }

        using var response = await client.SendAsync(RequestWithToken(factory, HttpMethod.Post, "/api/v1/demands", multipart));

        await AssertProblemAsync(response, factory, HttpStatusCode.BadRequest, "createDemand");
        store.DocumentCount.Should().Be(0);
        demands.Demands.Should().BeEmpty();
    }

    [Theory]
    [InlineData("text/plain", "%PDF-1.7\ncontent")]
    [InlineData("application/pdf", "not-a-pdf")]
    public async Task At26_InvalidPdfReturns422WithoutStorage(string contentType, string content)
    {
        using var factory = new KlesiaApiFactory();
        var users = CreateUserRepository(new ApplicationUser(OwnerUserId, OwnerOid, "Example User", true));
        var demands = new DemandRepository();
        var store = CreateDocumentStore();
        using var host = CreateHost(factory, users, demands, store);
        using var client = host.CreateClient();
        using var multipart = CreateMultipart(ValidGkNumber);
        AddPdf(multipart, Encoding.ASCII.GetBytes(content), contentType: contentType);

        using var response = await client.SendAsync(RequestWithToken(factory, HttpMethod.Post, "/api/v1/demands", multipart));

        await AssertProblemAsync(response, factory, HttpStatusCode.UnprocessableEntity, "createDemand");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("errors").TryGetProperty("bae_file", out _).Should().BeTrue();
        store.DocumentCount.Should().Be(0);
        demands.Demands.Should().BeEmpty();
    }

    [Fact]
    public async Task At27_FileLimitPlusOneReturns413ProblemAndDoesNotStore()
    {
        var oversizedPdf = new byte[10 * 1_048_576 + 1];
        Encoding.ASCII.GetBytes("%PDF-").CopyTo(oversizedPdf, 0);
        using var factory = new KlesiaApiFactory();
        var users = CreateUserRepository(new ApplicationUser(OwnerUserId, OwnerOid, "Example User", true));
        var demands = new DemandRepository();
        var store = CreateDocumentStore();
        using var host = CreateHost(factory, users, demands, store);
        using var client = host.CreateClient();

        using var response = await client.SendAsync(CreateRequest(factory, ValidGkNumber, oversizedPdf));

        await AssertProblemAsync(response, factory, (HttpStatusCode)413, "createDemand");
        store.DocumentCount.Should().Be(0);
        demands.Demands.Should().BeEmpty();
    }

    [Fact]
    public async Task At28_NonMultipartRequestReturns415Problem()
    {
        using var factory = new KlesiaApiFactory();
        using var host = CreateHost(
            factory,
            CreateUserRepository(new ApplicationUser(OwnerUserId, OwnerOid, "Example User", true)),
            new DemandRepository(),
            CreateDocumentStore());
        using var client = host.CreateClient();
        using var request = RequestWithToken(factory, HttpMethod.Post, "/api/v1/demands", new StringContent("not multipart"));

        using var response = await client.SendAsync(request);

        await AssertProblemAsync(response, factory, HttpStatusCode.UnsupportedMediaType, "createDemand");
    }

    [Fact]
    public async Task At29_UnauthorizedAndForbiddenRequestsDoNotStore()
    {
        using var factory = new KlesiaApiFactory();
        var users = Substitute.For<IUserRepository>();
        users.GetByUniqueIdAsync(OwnerOid, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ApplicationUser?>(null));
        var inactiveOid = Guid.Parse("44444444-4444-4444-4444-444444444444");
        users.GetByUniqueIdAsync(inactiveOid, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ApplicationUser?>(
                new ApplicationUser(OwnerUserId + 1, inactiveOid, "Inactive User", false)));
        var demands = new DemandRepository();
        var store = CreateDocumentStore();
        using var host = CreateHost(factory, users, demands, store);
        using var client = host.CreateClient();

        using var unauthorizedRequest = CreateRequest(factory, ValidGkNumber, PdfBytes);
        unauthorizedRequest.Headers.Authorization = null;
        using var unauthorized = await client.SendAsync(unauthorizedRequest);
        using var missingScope = await client.SendAsync(CreateRequest(
            factory,
            ValidGkNumber,
            PdfBytes,
            factory.Tokens.Create(TestTokenKind.MissingScope)));
        using var malformedMultipart = CreateMultipart(null);
        using var unknownUser = await client.SendAsync(RequestWithToken(
            factory,
            HttpMethod.Post,
            "/api/v1/demands",
            malformedMultipart,
            factory.Tokens.Create(oidOverride: "55555555-5555-5555-5555-555555555555")));
        using var inactiveUser = await client.SendAsync(CreateRequest(
            factory,
            ValidGkNumber,
            PdfBytes,
            factory.Tokens.Create(oidOverride: inactiveOid.ToString("D"))));

        await AssertProblemAsync(unauthorized, factory, HttpStatusCode.Unauthorized, "createDemand");
        await AssertProblemAsync(missingScope, factory, HttpStatusCode.Forbidden, "createDemand");
        await AssertProblemAsync(unknownUser, factory, HttpStatusCode.Forbidden, "createDemand");
        await AssertProblemAsync(inactiveUser, factory, HttpStatusCode.Forbidden, "createDemand");
        store.DocumentCount.Should().Be(0);
        demands.Demands.Should().BeEmpty();
    }

    [Fact]
    public async Task At30At56_SameGkCreatesTwoDemandsAndDistinctOwnerBlobNames()
    {
        using var factory = new KlesiaApiFactory();
        var users = CreateUserRepository(new ApplicationUser(OwnerUserId, OwnerOid, "Example User", true));
        var demands = new DemandRepository();
        var store = CreateDocumentStore();
        var notifier = new RecordingNotifier();
        var queue = Substitute.For<IExtractionQueue>();
        queue.TryEnqueue(Arg.Any<int>()).Returns(true);
        using var host = CreateHost(factory, users, demands, store, notifier, queue);
        using var client = host.CreateClient();

        using var first = await client.SendAsync(CreateRequest(factory, ValidGkNumber, PdfBytes));
        using var second = await client.SendAsync(CreateRequest(factory, ValidGkNumber, PdfBytes));

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        ContractValidator.AssertResponse(first, "createDemand");
        ContractValidator.AssertResponse(second, "createDemand");
        demands.Demands.Should().HaveCount(2);
        store.UploadedBlobUrls.Should().HaveCount(2);
        store.UploadedBlobUrls.Select(url => url.AbsolutePath)
            .Should().OnlyContain(path => path.StartsWith($"/bae/{OwnerOid:D}/", StringComparison.Ordinal));
        store.UploadedBlobUrls.Select(url => url.AbsolutePath).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task At48_StorageOutageReturns503AndInsertFailureDeletesUploadedBlob()
    {
        using var unavailableFactory = new KlesiaApiFactory();
        var unavailableStore = CreateDocumentStore();
        unavailableStore.IsUnavailable = true;
        var unavailableDemands = new DemandRepository();
        using var unavailableHost = CreateHost(
            unavailableFactory,
            CreateUserRepository(new ApplicationUser(OwnerUserId, OwnerOid, "Example User", true)),
            unavailableDemands,
            unavailableStore);
        using var unavailableClient = unavailableHost.CreateClient();

        using var unavailable = await unavailableClient.SendAsync(CreateRequest(unavailableFactory, ValidGkNumber, PdfBytes));

        await AssertProblemAsync(unavailable, unavailableFactory, HttpStatusCode.ServiceUnavailable, "createDemand");
        unavailableStore.DocumentCount.Should().Be(0);
        unavailableDemands.Demands.Should().BeEmpty();

        using var insertFactory = new KlesiaApiFactory();
        var insertStore = CreateDocumentStore();
        var insertDemands = new DemandRepository { InsertException = new InvalidOperationException("private insert failure") };
        using var insertHost = CreateHost(
            insertFactory,
            CreateUserRepository(new ApplicationUser(OwnerUserId, OwnerOid, "Example User", true)),
            insertDemands,
            insertStore);
        using var insertClient = insertHost.CreateClient();

        using var failedInsert = await insertClient.SendAsync(CreateRequest(insertFactory, ValidGkNumber, PdfBytes));

        await AssertProblemAsync(failedInsert, insertFactory, HttpStatusCode.InternalServerError, "createDemand");
        insertStore.DocumentCount.Should().Be(0);
        insertStore.DeleteCount.Should().Be(1);
        insertDemands.Demands.Should().BeEmpty();
    }

    private static InMemoryBaeDocumentStore CreateDocumentStore() => new();

    private static IUserRepository CreateUserRepository(ApplicationUser? user)
    {
        var users = Substitute.For<IUserRepository>();
        users.GetByUniqueIdAsync(OwnerOid, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(user));
        return users;
    }

    private static WebApplicationFactory<Program> CreateHost(
        KlesiaApiFactory factory,
        IUserRepository users,
        DemandRepository demands,
        InMemoryBaeDocumentStore store,
        INotifier? notifier = null,
        IExtractionQueue? queue = null)
    {
        return factory.WithServices(services =>
        {
            services.RemoveAll<IUserRepository>();
            services.RemoveAll<IDemandRepository>();
            services.RemoveAll<IBaeDocumentStore>();
            services.RemoveAll<INotifier>();
            services.RemoveAll<IExtractionQueue>();
            services.AddSingleton(users);
            services.AddSingleton<IDemandRepository>(demands);
            services.AddSingleton<IBaeDocumentStore>(store);
            services.AddSingleton(notifier ?? Substitute.For<INotifier>());
            services.AddSingleton(queue ?? Substitute.For<IExtractionQueue>());
        });
    }

    private static HttpRequestMessage CreateRequest(
        KlesiaApiFactory factory,
        string gkNumber,
        byte[] pdf,
        string? token = null)
    {
        var multipart = CreateMultipart(gkNumber);
        AddPdf(multipart, pdf);
        return RequestWithToken(factory, HttpMethod.Post, "/api/v1/demands", multipart, token);
    }

    private static MultipartFormDataContent CreateMultipart(string? gkNumber)
    {
        var multipart = new MultipartFormDataContent();
        if (gkNumber is not null)
        {
            multipart.Add(new StringContent(gkNumber), "gk_number");
        }

        return multipart;
    }

    private static void AddPdf(
        MultipartFormDataContent multipart,
        byte[] content,
        string filename = Filename,
        string contentType = "application/pdf")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(file, "bae_file", filename);
    }

    private static HttpRequestMessage RequestWithToken(
        KlesiaApiFactory factory,
        HttpMethod method,
        string path,
        HttpContent? content = null,
        string? token = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token ?? factory.Tokens.Create(oidOverride: OwnerOid.ToString("D")));
        return request;
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        KlesiaApiFactory factory,
        HttpStatusCode status,
        string operationId)
    {
        var responseBody = await response.Content.ReadAsStringAsync();
        var logMessages = string.Join(Environment.NewLine, factory.LogSink.Entries.Select(entry => entry.Message));
        response.StatusCode.Should().Be(status, $"Response: {responseBody}{Environment.NewLine}Logs: {logMessages}");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var correlationId = response.Headers.GetValues("X-Correlation-Id").Single();
        using var body = JsonDocument.Parse(responseBody);
        body.RootElement.GetProperty("status").GetInt32().Should().Be((int)status);
        body.RootElement.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        body.RootElement.GetProperty("traceId").GetString().Should().Be(correlationId);
        ContractValidator.AssertResponse(response, operationId);
    }

    private sealed class DemandRepository : IDemandRepository
    {
        private readonly List<Demand> _demands = [];
        private readonly List<Uri> _blobUrls = [];
        private int _nextId = 700;

        public Action? OnCreated { get; set; }

        public Exception? InsertException { get; set; }

        public IReadOnlyList<Demand> Demands => _demands;

        public IReadOnlyList<Uri> BlobUrls => _blobUrls;

        public Task<IReadOnlyList<Demand>> ListByUserAsync(int userId, CancellationToken cancellationToken)
        {
            IReadOnlyList<Demand> result = _demands
                .Where(demand => demand.UserId == userId)
                .OrderByDescending(demand => demand.Id)
                .ToArray();
            return Task.FromResult(result);
        }

        public Task<string?> GetOwnedBaeLinkAsync(int demandId, int userId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<int> CreatePendingAsync(
            int userId,
            GKNumber gkNumber,
            Uri blobUrl,
            CancellationToken cancellationToken)
        {
            if (InsertException is not null)
            {
                return Task.FromException<int>(InsertException);
            }

            var id = ++_nextId;
            _blobUrls.Add(blobUrl);
            _demands.Add(new Demand(id, userId, gkNumber, DemandStatus.Pending));
            OnCreated?.Invoke();
            return Task.FromResult(id);
        }
    }
}