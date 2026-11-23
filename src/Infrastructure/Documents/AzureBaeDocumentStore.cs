using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Klesia.Application.Abstractions;
using Klesia.Application.Documents;
using Klesia.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Klesia.Infrastructure.Documents;

public sealed class AzureBaeDocumentStore : IBaeDocumentStore
{
    private readonly BlobContainerClient _containerClient;
    private readonly Uri _blobServiceUri;
    private readonly string _containerName;

    public AzureBaeDocumentStore(BlobServiceClient blobServiceClient, IOptions<StorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(blobServiceClient);
        ArgumentNullException.ThrowIfNull(options);

        _blobServiceUri = new Uri(options.Value.BlobServiceUri, UriKind.Absolute);
        _containerName = options.Value.ContainerName;
        _containerClient = blobServiceClient.GetBlobContainerClient(_containerName);
    }

    public bool IsOwnedLocation(Uri blobUrl) =>
        BlobDocumentLocation.IsOwnedLocation(blobUrl, _blobServiceUri, _containerName);

    public async Task<Uri> UploadAsync(Guid ownerOid, Stream pdf, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        var blobClient = _containerClient.GetBlobClient(
            BlobDocumentLocation.CreateBlobName(ownerOid, Guid.NewGuid()));

        try
        {
            await blobClient.UploadAsync(
                pdf,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/pdf" },
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
                },
                cancellationToken);

            return blobClient.Uri;
        }
        catch (RequestFailedException exception) when (IsUnavailable(exception.Status))
        {
            throw new DocumentStoreUnavailableException();
        }
        catch (RequestFailedException)
        {
            throw new InvalidOperationException("Blob Storage upload failed.");
        }
        catch (HttpRequestException)
        {
            throw new DocumentStoreUnavailableException();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DocumentStoreUnavailableException();
        }
    }

    public async Task<BaeDocumentReadResult> OpenReadAsync(Uri blobUrl, CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(blobUrl);

        try
        {
            var download = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return new BaeDocumentReadResult(download.Value.Content, download.Value.Details.ContentLength);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            throw new DocumentNotFoundException();
        }
        catch (RequestFailedException exception) when (IsUnavailable(exception.Status))
        {
            throw new DocumentStoreUnavailableException();
        }
        catch (RequestFailedException)
        {
            throw new InvalidOperationException("Blob Storage read failed.");
        }
        catch (HttpRequestException)
        {
            throw new DocumentStoreUnavailableException();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DocumentStoreUnavailableException();
        }
    }

    public async Task DeleteAsync(Uri blobUrl, CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(blobUrl);

        try
        {
            await blobClient.DeleteIfExistsAsync(
                DeleteSnapshotsOption.IncludeSnapshots,
                cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (IsUnavailable(exception.Status))
        {
            throw new DocumentStoreUnavailableException();
        }
        catch (RequestFailedException)
        {
            throw new InvalidOperationException("Blob Storage delete failed.");
        }
        catch (HttpRequestException)
        {
            throw new DocumentStoreUnavailableException();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DocumentStoreUnavailableException();
        }
    }

    private BlobClient GetBlobClient(Uri blobUrl)
    {
        if (!BlobDocumentLocation.TryGetBlobName(blobUrl, _blobServiceUri, _containerName, out var blobName))
        {
            throw new ArgumentException("The blob URL is outside the configured storage container.", nameof(blobUrl));
        }

        return _containerClient.GetBlobClient(blobName);
    }

    private static bool IsUnavailable(int status) => status <= 0 || status >= 500;
}