using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using IntegrationTests.Contract;
using IntegrationTests.Fakes;
using Klesia.Application.Abstractions;
using Klesia.Application.Authorization;
using Klesia.Application.Demands;
using Klesia.Domain.Users;
using Klesia.WebApi.Authentication;
using Klesia.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace IntegrationTests;

public sealed class DemandDocumentEndpointTests
{
    private static readonly Guid OwnerOid = Guid.Parse(TestTokenFactory.Oid);
    private const int OwnerUserId = 301;
    private const int DemandId = 51;
    private const string ApprovedOrigin = "http://localhost:5173";
    private const string StoredBlobUrl = "https://test.blob.core.windows.net/bae/owner/document.pdf";

    [Fact]
    public async Task At51_OwnDocumentStreamsExactBytesAndHeaders()
    {
        var pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\nowned document\n%%EOF");
        using var factory = new KlesiaApiFactory();
        var users = CreateActiveUserRepository();
        var demands = CreateDemandRepository(StoredBlobUrl);
        var store = CreateDocumentStore();
        store.AddDocument(new Uri(StoredBlobUrl), pdf);
        using var host = CreateHost(factory, users, demands, store);
        using var client = host.CreateClient();

        using var response = await client.SendAsync(RequestWithToken(factory.Tokens.Create()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentLength.Should().Be(pdf.LongLength);
        response.Content.Headers.ContentDisposition?.ToString()
            .Should().Be($"inline; filename=\"bae-{DemandId}.pdf\"");
        response.Headers.CacheControl?.Private.Should().BeTrue();
        response.Headers.CacheControl?.NoStore.Should().BeTrue();
        response.Headers.GetValues("X-Content-Type-Options").Single().Should().Be("nosniff");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(pdf);
        ContractValidator.AssertResponse(response, "getDemandDocument");
    }

    [Fact]
    public async Task At52_MissingAndForeignDemandsReturnIdentical404WithoutOpeningStorage()
    {
        using var factory = new KlesiaApiFactory();
        var users = CreateActiveUserRepository();
        var demands = Substitute.For<IDemandRepository>();
        demands.GetOwnedBaeLinkAsync(Arg.Any<int>(), OwnerUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));
        var store = CreateDocumentStore();
        using var host = CreateHost(factory, users, demands, store);
        using var client = host.CreateClient();

        using var missingResponse = await client.SendAsync(RequestWithToken(factory.Tokens.Create(), DemandId));
        using var foreignResponse = await client.SendAsync(RequestWithToken(factory.Tokens.Create(), DemandId + 1));

        await AssertProblemResponseAsync(missingResponse, HttpStatusCode.NotFound);
        await AssertProblemResponseAsync(foreignResponse, HttpStatusCode.NotFound);
        ContractValidator.AssertResponse(missingResponse, "getDemandDocument");
        ContractValidator.AssertResponse(foreignResponse, "getDemandDocument");
        (await ProblemSignatureAsync(missingResponse)).Should().Be(await ProblemSignatureAsync(foreignResponse));
        store.OpenReadCount.Should().Be(0);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task At52_InvalidDemandIdReturns404ForAuthenticatedCaller(string demandId)
    {
        using var factory = new KlesiaApiFactory();
        var users = CreateActiveUserRepository();
        var demands = Substitute.For<IDemandRepository>();
        var store = CreateDocumentStore();
        using var host = CreateHost(factory, users, demands, store);
        using var client = host.CreateClient();
        using var request = RequestWithToken(factory.Tokens.Create(), demandId);

        using var response = await client.SendAsync(request);

        await AssertProblemResponseAsync(response, HttpStatusCode.NotFound);
        ContractValidator.AssertResponse(response, "getDemandDocument");
        store.OpenReadCount.Should().Be(0);
        await demands.DidNotReceive().GetOwnedBaeLinkAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task At53_UnauthorizedAndForbiddenRequestsDoNotOpenStorage()
    {
        using var factory = new KlesiaApiFactory();
        var users = CreateActiveUserRepository();
        var demands = Substitute.For<IDemandRepository>();
        var store = CreateDocumentStore();
        using var host = CreateHost(factory, users, demands, store);
        using var client = host.CreateClient();

        using var unauthorized = await client.GetAsync($"/api/v1/demands/{DemandId}/bae");
        using var forbidden = await client.SendAsync(RequestWithToken(
            factory.Tokens.Create(TestTokenKind.MissingScope)));

        await AssertProblemResponseAsync(unauthorized, HttpStatusCode.Unauthorized);
        await AssertProblemResponseAsync(forbidden, HttpStatusCode.Forbidden);
        ContractValidator.AssertResponse(unauthorized, "getDemandDocument");
        ContractValidator.AssertResponse(forbidden, "getDemandDocument");
        store.OpenReadCount.Should().Be(0);
    }

    [Theory]
    [InlineData("http://test.blob.core.windows.net/bae/owner/document.pdf")]
    [InlineData("https://foreign.blob.core.windows.net/bae/owner/document.pdf")]
    [InlineData("https://test.blob.core.windows.net/other/owner/document.pdf")]
    [InlineData("https://test.blob.core.windows.net/bae/owner/document.pdf?sig=sensitive")]
    public async Task At19_ForeignOrMalformedStoredBlobUrlReturnsSanitized500(string blobUrl)
    {
        using var factory = new KlesiaApiFactory();
        var users = CreateActiveUserRepository();
        var demands = CreateDemandRepository(blobUrl);
        var store = CreateDocumentStore();
        using var host = CreateHost(factory, users, demands, store);
        using var client = host.CreateClient();
        factory.LogSink.Clear();

        using var response = await client.SendAsync(RequestWithToken(factory.Tokens.Create()));

        await AssertProblemResponseAsync(response, HttpStatusCode.InternalServerError);
        ContractValidator.AssertResponse(response, "getDemandDocument");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(blobUrl).And.NotContain("sensitive");
        string.Join(Environment.NewLine, factory.LogSink.Entries.Select(entry => entry.Message))
            .Should().NotContain(blobUrl);
        store.OpenReadCount.Should().Be(0);
    }

    [Fact]
    public async Task At54_MissingBlobReturnsSanitized500AndStorageOutageReturns503()
    {
        using var missingFactory = new KlesiaApiFactory();
        var missingStore = CreateDocumentStore();
        missingStore.ReturnNotFoundOnRead = true;
        using var missingHost = CreateHost(
            missingFactory,
            CreateActiveUserRepository(),
            CreateDemandRepository(StoredBlobUrl),
            missingStore);
        using var missingClient = missingHost.CreateClient();

        using var missingResponse = await missingClient.SendAsync(RequestWithToken(missingFactory.Tokens.Create()));

        await AssertProblemResponseAsync(missingResponse, HttpStatusCode.InternalServerError);
        ContractValidator.AssertResponse(missingResponse, "getDemandDocument");
        (await missingResponse.Content.ReadAsStringAsync()).Should().NotContain(StoredBlobUrl);

        using var unavailableFactory = new KlesiaApiFactory();
        var unavailableStore = CreateDocumentStore();
        unavailableStore.IsUnavailable = true;
        using var unavailableHost = CreateHost(
            unavailableFactory,
            CreateActiveUserRepository(),
            CreateDemandRepository(StoredBlobUrl),
            unavailableStore);
        using var unavailableClient = unavailableHost.CreateClient();

        using var unavailableResponse = await unavailableClient.SendAsync(RequestWithToken(unavailableFactory.Tokens.Create()));

        await AssertProblemResponseAsync(unavailableResponse, HttpStatusCode.ServiceUnavailable);
        ContractValidator.AssertResponse(unavailableResponse, "getDemandDocument");
        (await unavailableResponse.Content.ReadAsStringAsync()).Should().NotContain(StoredBlobUrl);
    }

    [Fact]
    public async Task At58_LargeDocumentIsCopiedInChunksWithoutFullFileBuffering()
    {
        var pdf = new byte[10 * 1024 * 1024];
        "%PDF-1.7"u8.CopyTo(pdf);
        var trackingStream = new TestReadStream(pdf, maxChunkSize: 4096);
        using var factory = new KlesiaApiFactory();
        var store = CreateDocumentStore();
        store.AddDocument(new Uri(StoredBlobUrl), pdf);
        store.ReadStreamFactory = _ => trackingStream;
        using var host = CreateHost(
            factory,
            CreateActiveUserRepository(),
            CreateDemandRepository(StoredBlobUrl),
            store);
        using var client = host.CreateClient();

        using var response = await client.SendAsync(RequestWithToken(factory.Tokens.Create()));
        var receivedBytes = await response.Content.ReadAsByteArrayAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        receivedBytes.Should().Equal(pdf);
        trackingStream.BytesRead.Should().Be(pdf.Length);
        trackingStream.ReadCount.Should().BeGreaterThan(1);
        trackingStream.MaximumReadSize.Should().BeLessThan(pdf.Length);
    }

    [Fact]
    public async Task At58_RequestCancellationStopsReadingTheDocumentStream()
    {
        var pdf = new byte[1024 * 1024];
        var blockingStream = new TestReadStream(pdf, maxChunkSize: 4096, blockAfterFirstRead: true);
        var users = CreateActiveUserRepository();
        var demands = CreateDemandRepository(StoredBlobUrl);
        var store = Substitute.For<IBaeDocumentStore>();
        store.IsOwnedLocation(new Uri(StoredBlobUrl)).Returns(true);
        store.OpenReadAsync(new Uri(StoredBlobUrl), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new Klesia.Application.Documents.BaeDocumentReadResult(
                blockingStream,
                null)));
        var service = new DemandService(
            new AuthorizedUserResolver(users),
            demands,
            store,
            Substitute.For<INotifier>(),
            Substitute.For<IExtractionQueue>(),
            new CreateDemandValidator(),
            Substitute.For<IDemandCreationSettings>(),
            NullLogger<DemandService>.Instance);
        var principal = Substitute.For<ICurrentPrincipal>();
        principal.GetOid(Arg.Any<System.Security.Claims.ClaimsPrincipal>()).Returns(OwnerOid);
        var controller = new DemandsController(service, principal, NullLogger<DemandsController>.Instance);
        using var cancellation = new CancellationTokenSource();
        var httpContext = new DefaultHttpContext();
        httpContext.RequestAborted = cancellation.Token;
        httpContext.Response.Body = new MemoryStream();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var action = controller.GetDocumentAsync(DemandId, cancellation.Token);
        await blockingStream.FirstReadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await action.WaitAsync(TimeSpan.FromSeconds(5));
        await blockingStream.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        blockingStream.BytesRead.Should().BeLessThan(pdf.Length);
    }

    [Fact]
    public async Task At59_DocumentRouteAllowsApprovedCorsPreflight()
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, $"/api/v1/demands/{DemandId}/bae");
        request.Headers.TryAddWithoutValidation("Origin", ApprovedOrigin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "authorization");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Single()
            .Should().Be(ApprovedOrigin);
        response.Headers.GetValues("Access-Control-Allow-Methods").Single()
            .Should().Contain("GET");
    }

    private static WebApplicationFactory<Program> CreateHost(
        KlesiaApiFactory factory,
        IUserRepository users,
        IDemandRepository demands,
        IBaeDocumentStore store)
    {
        return factory.WithServices(services =>
        {
            services.RemoveAll<IUserRepository>();
            services.RemoveAll<IDemandRepository>();
            services.RemoveAll<IBaeDocumentStore>();
            services.AddSingleton(users);
            services.AddSingleton(demands);
            services.AddSingleton(store);
        });
    }

    private static IUserRepository CreateActiveUserRepository()
    {
        var users = Substitute.For<IUserRepository>();
        users.GetByUniqueIdAsync(OwnerOid, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ApplicationUser?>(
                new ApplicationUser(OwnerUserId, OwnerOid, "Example User", true)));
        return users;
    }

    private static IDemandRepository CreateDemandRepository(string? blobUrl)
    {
        var demands = Substitute.For<IDemandRepository>();
        demands.GetOwnedBaeLinkAsync(DemandId, OwnerUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(blobUrl));
        return demands;
    }

    private static InMemoryBaeDocumentStore CreateDocumentStore() => new();

    private static HttpRequestMessage RequestWithToken(string token, int demandId = DemandId) =>
        RequestWithToken(token, demandId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static HttpRequestMessage RequestWithToken(string token, string demandId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/demands/{demandId}/bae");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task AssertProblemResponseAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var correlationId = response.Headers.GetValues("X-Correlation-Id").Single();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("status").GetInt32().Should().Be((int)status);
        document.RootElement.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        document.RootElement.GetProperty("traceId").GetString().Should().Be(correlationId);
    }

    private static async Task<string> ProblemSignatureAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return $"{document.RootElement.GetProperty("title").GetString()}|{document.RootElement.GetProperty("status").GetInt32()}";
    }

    private sealed class TestReadStream : Stream
    {
        private readonly byte[] _content;
        private readonly int _maxChunkSize;
        private readonly bool _blockAfterFirstRead;
        private int _offset;
        private int _readCount;
        private int _maximumReadSize;

        public TestReadStream(byte[] content, int maxChunkSize, bool blockAfterFirstRead = false)
        {
            _content = content;
            _maxChunkSize = maxChunkSize;
            _blockAfterFirstRead = blockAfterFirstRead;
        }

        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstReadCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int BytesRead => _offset;

        public int ReadCount => _readCount;

        public int MaximumReadSize => _maximumReadSize;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _content.LongLength;

        public override long Position
        {
            get => _offset;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("The test stream only supports asynchronous reads.");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return ReadCoreAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadCoreAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private async ValueTask<int> ReadCoreAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_blockAfterFirstRead && _readCount > 0)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    CancellationObserved.TrySetResult();
                    throw;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var readLength = Math.Min(Math.Min(buffer.Length, _maxChunkSize), _content.Length - _offset);
            if (readLength <= 0)
            {
                return 0;
            }

            _content.AsMemory(_offset, readLength).CopyTo(buffer);
            _offset += readLength;
            _readCount++;
            _maximumReadSize = Math.Max(_maximumReadSize, readLength);
            FirstReadCompleted.TrySetResult();
            return readLength;
        }
    }
}