using System.Text;
using FluentAssertions;
using Klesia.Application.Abstractions;
using Klesia.Application.Authorization;
using Klesia.Application.Demands;
using Klesia.Application.Documents;
using Klesia.Domain.Demands;
using Klesia.Domain.Users;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace UnitTests.Demands;

public sealed class DemandServiceTests
{
    private static readonly Guid OwnerOid = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");
    private static readonly Uri FirstBlobUrl = new("https://storage.example/owner/first.pdf");
    private static readonly Uri SecondBlobUrl = new("https://storage.example/owner/second.pdf");
    private const int InternalUserId = 37;
    private const int DemandId = 81;
    private const string ValidGkNumber = "GK260902-144743112";
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\ncontent");

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IDemandRepository _demandRepository = Substitute.For<IDemandRepository>();
    private readonly IBaeDocumentStore _documentStore = Substitute.For<IBaeDocumentStore>();
    private readonly INotifier _notifier = Substitute.For<INotifier>();
    private readonly IExtractionQueue _extractionQueue = Substitute.For<IExtractionQueue>();
    private readonly IDemandCreationSettings _creationSettings = Substitute.For<IDemandCreationSettings>();

    public DemandServiceTests()
    {
        _creationSettings.MaxPdfSizeBytes.Returns(1_048_576);
    }

    [Fact]
    public async Task CreateAsync_ValidInput_CommitsBeforeNotificationAndEnqueue()
    {
        ConfigureActiveUser();
        var calls = new List<string>();
        _documentStore.UploadAsync(OwnerOid, Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("upload");
                return Task.FromResult(FirstBlobUrl);
            });
        _demandRepository.CreatePendingAsync(
                InternalUserId,
                Arg.Is<GKNumber>(number => number.Value == ValidGkNumber),
                FirstBlobUrl,
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("insert");
                return Task.FromResult(DemandId);
            });
        _notifier.NotifyDemandsChangedAsync(OwnerOid, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("notify");
                return Task.CompletedTask;
            });
        _extractionQueue.TryEnqueue(DemandId).Returns(_ =>
        {
            calls.Add("enqueue");
            return true;
        });

        using var content = new MemoryStream(PdfBytes);
        var result = await CreateSut().CreateAsync(
            OwnerOid,
            ValidInput(content),
            CancellationToken.None);

        calls.Should().Equal("upload", "insert", "notify", "enqueue");
        var created = result.Should().BeOfType<CreateDemandResult.Created>().Subject;
        created.Demand.Should().Be(new Demand(
            DemandId,
            InternalUserId,
            GKNumber.Create(ValidGkNumber),
            DemandStatus.Pending));
        await _userRepository.Received(1).GetByUniqueIdAsync(OwnerOid, Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyDemandsChangedAsync(OwnerOid, Arg.Any<CancellationToken>());
        _extractionQueue.Received(1).TryEnqueue(DemandId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateAsync_UnknownOrInactiveUserDoesNotTouchDemandDependencies(bool userExists)
    {
        _userRepository.GetByUniqueIdAsync(OwnerOid, Arg.Any<CancellationToken>())
            .Returns(userExists
                ? new ApplicationUser(InternalUserId, OwnerOid, "Inactive", false)
                : null);
        using var content = new MemoryStream(PdfBytes);

        var act = () => CreateSut().CreateAsync(OwnerOid, ValidInput(content), CancellationToken.None);

        await act.Should().ThrowAsync<AccessDeniedException>();
        await _documentStore.DidNotReceive()
            .UploadAsync(Arg.Any<Guid>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        await _demandRepository.DidNotReceive()
            .CreatePendingAsync(Arg.Any<int>(), Arg.Any<GKNumber>(), Arg.Any<Uri>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive()
            .NotifyDemandsChangedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _extractionQueue.DidNotReceive().TryEnqueue(Arg.Any<int>());
    }

    [Fact]
    public async Task CreateAsync_InvalidGkDoesNotUploadOrInsert()
    {
        ConfigureActiveUser();
        using var content = new MemoryStream(PdfBytes);

        var result = await CreateSut().CreateAsync(
            OwnerOid,
            ValidInput(content, "GK260902144743112"),
            CancellationToken.None);

        result.Should().BeOfType<CreateDemandResult.Invalid>()
            .Which.Errors.Should().ContainKey("gk_number");
        await _documentStore.DidNotReceive()
            .UploadAsync(Arg.Any<Guid>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        await _demandRepository.DidNotReceive()
            .CreatePendingAsync(Arg.Any<int>(), Arg.Any<GKNumber>(), Arg.Any<Uri>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive()
            .NotifyDemandsChangedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _extractionQueue.DidNotReceive().TryEnqueue(Arg.Any<int>());
    }

    [Fact]
    public async Task CreateAsync_StorageUnavailableReturnsUnavailableWithoutInserting()
    {
        ConfigureActiveUser();
        _documentStore.UploadAsync(OwnerOid, Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns<Task<Uri>>(_ => throw new DocumentStoreUnavailableException());
        using var content = new MemoryStream(PdfBytes);

        var result = await CreateSut().CreateAsync(OwnerOid, ValidInput(content), CancellationToken.None);

        result.Should().BeOfType<CreateDemandResult.StorageUnavailable>();
        await _demandRepository.DidNotReceive()
            .CreatePendingAsync(Arg.Any<int>(), Arg.Any<GKNumber>(), Arg.Any<Uri>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive()
            .NotifyDemandsChangedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _extractionQueue.DidNotReceive().TryEnqueue(Arg.Any<int>());
    }

    [Fact]
    public async Task CreateAsync_InsertFailureAttemptsCleanupAndDoesNotNotifyOrEnqueue()
    {
        ConfigureActiveUser();
        ConfigureUpload(FirstBlobUrl);
        _demandRepository.CreatePendingAsync(
                Arg.Any<int>(), Arg.Any<GKNumber>(), Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("Insert failed."));
        using var content = new MemoryStream(PdfBytes);

        var act = () => CreateSut().CreateAsync(OwnerOid, ValidInput(content), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Insert failed.");
        await _documentStore.Received(1).DeleteAsync(FirstBlobUrl, Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive()
            .NotifyDemandsChangedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _extractionQueue.DidNotReceive().TryEnqueue(Arg.Any<int>());
    }

    [Fact]
    public async Task CreateAsync_CleanupFailureIsLoggedWithoutBlobUrlAndInsertErrorIsRethrown()
    {
        ConfigureActiveUser();
        ConfigureUpload(FirstBlobUrl);
        _demandRepository.CreatePendingAsync(
                Arg.Any<int>(), Arg.Any<GKNumber>(), Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("Insert failed."));
        _documentStore.DeleteAsync(FirstBlobUrl, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException($"Cleanup failed for {FirstBlobUrl}"));
        var logger = new CapturingLogger<DemandService>();
        using var content = new MemoryStream(PdfBytes);

        var act = () => CreateSut(logger).CreateAsync(OwnerOid, ValidInput(content), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Insert failed.");
        logger.Messages.Should().ContainSingle();
        logger.Messages.Single().Should().NotContain(FirstBlobUrl.ToString());
    }

    [Fact]
    public async Task CreateAsync_NotificationFailureStillReturnsCreatedAndEnqueues()
    {
        ConfigureActiveUser();
        ConfigureUpload(FirstBlobUrl);
        ConfigureInsert(DemandId);
        _notifier.NotifyDemandsChangedAsync(OwnerOid, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Notification failed."));
        _extractionQueue.TryEnqueue(DemandId).Returns(true);
        using var content = new MemoryStream(PdfBytes);

        var result = await CreateSut().CreateAsync(OwnerOid, ValidInput(content), CancellationToken.None);

        result.Should().BeOfType<CreateDemandResult.Created>();
        _extractionQueue.Received(1).TryEnqueue(DemandId);
    }

    [Fact]
    public async Task CreateAsync_FullQueueStillReturnsCreated()
    {
        ConfigureActiveUser();
        ConfigureUpload(FirstBlobUrl);
        ConfigureInsert(DemandId);
        _extractionQueue.TryEnqueue(DemandId).Returns(false);
        using var content = new MemoryStream(PdfBytes);

        var result = await CreateSut().CreateAsync(OwnerOid, ValidInput(content), CancellationToken.None);

        result.Should().BeOfType<CreateDemandResult.Created>();
        _extractionQueue.Received(1).TryEnqueue(DemandId);
    }

    [Fact]
    public async Task CreateAsync_QueueFailureStillReturnsCreated()
    {
        ConfigureActiveUser();
        ConfigureUpload(FirstBlobUrl);
        ConfigureInsert(DemandId);
        _extractionQueue.TryEnqueue(DemandId).Returns(_ => throw new InvalidOperationException("Queue failed."));
        using var content = new MemoryStream(PdfBytes);

        var result = await CreateSut().CreateAsync(OwnerOid, ValidInput(content), CancellationToken.None);

        result.Should().BeOfType<CreateDemandResult.Created>();
        _extractionQueue.Received(1).TryEnqueue(DemandId);
    }

    [Fact]
    public async Task CreateAsync_SameGkSubmittedTwiceCreatesTwoDemandsWithDistinctBlobUrls()
    {
        ConfigureActiveUser();
        var uploadCount = 0;
        _documentStore.UploadAsync(OwnerOid, Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(uploadCount++ == 0 ? FirstBlobUrl : SecondBlobUrl));
        var insertedUrls = new List<Uri>();
        _demandRepository.CreatePendingAsync(
                InternalUserId,
                Arg.Is<GKNumber>(number => number.Value == ValidGkNumber),
                Arg.Any<Uri>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                insertedUrls.Add(call.ArgAt<Uri>(2));
                return Task.FromResult(DemandId + insertedUrls.Count - 1);
            });
        _extractionQueue.TryEnqueue(Arg.Any<int>()).Returns(true);

        using var firstContent = new MemoryStream(PdfBytes);
        using var secondContent = new MemoryStream(PdfBytes);
        var firstResult = await CreateSut().CreateAsync(OwnerOid, ValidInput(firstContent), CancellationToken.None);
        var secondResult = await CreateSut().CreateAsync(OwnerOid, ValidInput(secondContent), CancellationToken.None);

        firstResult.Should().BeOfType<CreateDemandResult.Created>();
        secondResult.Should().BeOfType<CreateDemandResult.Created>();
        insertedUrls.Should().Equal(FirstBlobUrl, SecondBlobUrl);
        insertedUrls.Distinct().Should().HaveCount(2);
        await _demandRepository.Received(2).CreatePendingAsync(
            InternalUserId,
            Arg.Is<GKNumber>(number => number.Value == ValidGkNumber),
            Arg.Any<Uri>(),
            Arg.Any<CancellationToken>());
    }

    private DemandService CreateSut(ILogger<DemandService>? logger = null) => new(
        new AuthorizedUserResolver(_userRepository),
        _demandRepository,
        _documentStore,
        _notifier,
        _extractionQueue,
        new CreateDemandValidator(),
        _creationSettings,
        logger ?? Substitute.For<ILogger<DemandService>>());

    private void ConfigureActiveUser() =>
        _userRepository.GetByUniqueIdAsync(OwnerOid, Arg.Any<CancellationToken>())
            .Returns(new ApplicationUser(InternalUserId, OwnerOid, "Active", true));

    private void ConfigureUpload(Uri blobUrl) =>
        _documentStore.UploadAsync(OwnerOid, Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(blobUrl));

    private void ConfigureInsert(int id) =>
        _demandRepository.CreatePendingAsync(
                InternalUserId, Arg.Any<GKNumber>(), Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(id));

    private static CreateDemandInput ValidInput(Stream content, string gkNumber = ValidGkNumber) =>
        new(gkNumber, "application/pdf", PdfBytes.LongLength, content);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}