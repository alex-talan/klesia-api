using Klesia.Application.Documents;

namespace Klesia.Application.Abstractions;

public interface IBaeDocumentStore
{
    public Task<Uri> UploadAsync(Guid ownerOid, Stream pdf, CancellationToken cancellationToken);

    public Task<BaeDocumentReadResult> OpenReadAsync(Uri blobUrl, CancellationToken cancellationToken);

    public Task DeleteAsync(Uri blobUrl, CancellationToken cancellationToken);

    public bool IsOwnedLocation(Uri blobUrl);
}