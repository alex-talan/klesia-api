namespace Klesia.Infrastructure.Documents;

public static class BlobDocumentLocation
{
    public static string CreateBlobName(Guid ownerOid, Guid blobId) =>
        $"{ownerOid:D}/{blobId:D}.pdf";

    public static bool IsOwnedLocation(Uri blobUrl, Uri blobServiceUri, string containerName) =>
        TryGetBlobName(blobUrl, blobServiceUri, containerName, out _);

    public static bool TryGetBlobName(
        Uri blobUrl,
        Uri blobServiceUri,
        string containerName,
        out string blobName)
    {
        blobName = string.Empty;
        if (!blobUrl.IsAbsoluteUri
            || !blobServiceUri.IsAbsoluteUri
            || !string.Equals(blobUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(blobServiceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(blobUrl.Host, blobServiceUri.Host, StringComparison.OrdinalIgnoreCase)
            || blobUrl.Port != blobServiceUri.Port
            || !string.IsNullOrEmpty(blobUrl.UserInfo)
            || !string.IsNullOrEmpty(blobUrl.Query)
            || !string.IsNullOrEmpty(blobUrl.Fragment)
            || string.IsNullOrWhiteSpace(containerName)
            || HasQueryOrFragment(blobUrl.OriginalString)
            || ContainsTraversalSegment(blobUrl.OriginalString))
        {
            return false;
        }

        var path = Uri.UnescapeDataString(blobUrl.AbsolutePath);
        var prefix = $"/{containerName}/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)
            || path.EndsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        blobName = path[prefix.Length..];
        return blobName.Split('/').All(segment => segment.Length > 0);
    }

    private static bool ContainsTraversalSegment(string originalUrl)
    {
        var schemeSeparator = originalUrl.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator < 0)
        {
            return true;
        }

        var pathStart = originalUrl.IndexOf('/', schemeSeparator + 3);
        if (pathStart < 0)
        {
            return true;
        }

        var pathEnd = originalUrl.IndexOfAny(new[] { '?', '#' }, pathStart);
        var encodedPath = pathEnd < 0
            ? originalUrl[pathStart..]
            : originalUrl[pathStart..pathEnd];
        var decodedPath = Uri.UnescapeDataString(encodedPath);

        return decodedPath.Split('/').Any(segment => segment is "." or "..");
    }

    private static bool HasQueryOrFragment(string originalUrl)
    {
        var schemeSeparator = originalUrl.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator < 0)
        {
            return true;
        }

        var pathStart = originalUrl.IndexOf('/', schemeSeparator + 3);
        return pathStart >= 0 && originalUrl.IndexOfAny(new[] { '?', '#' }, pathStart) >= 0;
    }
}