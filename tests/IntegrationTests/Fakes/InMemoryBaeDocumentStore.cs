using System.Collections.Concurrent;
using Klesia.Application.Abstractions;
using Klesia.Application.Documents;
using Klesia.Infrastructure.Documents;

namespace IntegrationTests.Fakes;

public sealed class InMemoryBaeDocumentStore : IBaeDocumentStore
{
    private readonly ConcurrentDictionary<Uri, byte[]> _documents = new();
    private readonly Uri _blobServiceUri;
    private readonly string _containerName;
    private int _openReadCount;
    private int _deleteCount;

    public InMemoryBaeDocumentStore(
        Uri? blobServiceUri = null,
        string containerName = "bae")
    {
        _blobServiceUri = blobServiceUri ?? new Uri("https://test.blob.core.windows.net/");
        _containerName = containerName;
    }

    public bool IsUnavailable { get; set; }

    public bool ReturnNotFoundOnRead { get; set; }

    public int OpenReadCount => Volatile.Read(ref _openReadCount);

    public int DocumentCount => _documents.Count;

    public int DeleteCount => Volatile.Read(ref _deleteCount);

    public IReadOnlyCollection<Uri> UploadedBlobUrls => _documents.Keys.ToArray();

    public Func<byte[], Stream>? ReadStreamFactory { get; set; }

    public void AddDocument(Uri blobUrl, byte[] content)
    {
        if (!_documents.TryAdd(blobUrl, content))
        {
            throw new InvalidOperationException("A document already exists at that test URL.");
        }
    }

    public async Task<Uri> UploadAsync(Guid ownerOid, Stream pdf, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();

        using var buffer = new MemoryStream();
        await pdf.CopyToAsync(buffer, cancellationToken);

        var blobName = BlobDocumentLocation.CreateBlobName(ownerOid, Guid.NewGuid());
        var blobUrl = new Uri(_blobServiceUri, $"{_containerName}/{blobName}");
        if (!_documents.TryAdd(blobUrl, buffer.ToArray()))
        {
            throw new InvalidOperationException("A generated blob name already exists.");
        }

        return blobUrl;
    }

    public Task<BaeDocumentReadResult> OpenReadAsync(Uri blobUrl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _openReadCount);
        ThrowIfUnavailable();

        if (!IsOwnedLocation(blobUrl))
        {
            throw new ArgumentException("The blob URL is outside the configured storage container.", nameof(blobUrl));
        }

        if (ReturnNotFoundOnRead || !_documents.TryGetValue(blobUrl, out var content))
        {
            throw new DocumentNotFoundException();
        }

        Stream stream = ReadStreamFactory?.Invoke(content) ?? new MemoryStream(content, writable: false);
        long? length = stream.CanSeek ? stream.Length : null;
        return Task.FromResult(new BaeDocumentReadResult(stream, length));
    }

    public Task DeleteAsync(Uri blobUrl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();
        Interlocked.Increment(ref _deleteCount);

        if (!IsOwnedLocation(blobUrl))
        {
            throw new ArgumentException("The blob URL is outside the configured storage container.", nameof(blobUrl));
        }

        _documents.TryRemove(blobUrl, out _);
        return Task.CompletedTask;
    }

    public bool IsOwnedLocation(Uri blobUrl) =>
        BlobDocumentLocation.IsOwnedLocation(blobUrl, _blobServiceUri, _containerName);

    private void ThrowIfUnavailable()
    {
        if (IsUnavailable)
        {
            throw new DocumentStoreUnavailableException();
        }
    }
}