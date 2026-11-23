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

    public InMemoryBaeDocumentStore(
        Uri? blobServiceUri = null,
        string containerName = "bae")
    {
        _blobServiceUri = blobServiceUri ?? new Uri("https://test.blob.core.windows.net/");
        _containerName = containerName;
    }

    public bool IsUnavailable { get; set; }

    public bool ReturnNotFoundOnRead { get; set; }

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
        ThrowIfUnavailable();

        if (!IsOwnedLocation(blobUrl))
        {
            throw new ArgumentException("The blob URL is outside the configured storage container.", nameof(blobUrl));
        }

        if (ReturnNotFoundOnRead || !_documents.TryGetValue(blobUrl, out var content))
        {
            throw new DocumentNotFoundException();
        }

        Stream stream = new MemoryStream(content, writable: false);
        return Task.FromResult(new BaeDocumentReadResult(stream, content.LongLength));
    }

    public Task DeleteAsync(Uri blobUrl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();

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