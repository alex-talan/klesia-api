using Application.Abstractions;

namespace Infrastructure.Documents;

/// <summary>
/// The PDFs live in SharePoint and <c>Demand.BaeLink</c> stores the document's absolute HTTPS URL.
/// SharePoint permissions are the access gate: the browser opens the link with the user's own Microsoft
/// Entra single sign-on session, so no token is attached by the frontend and no signed or anonymous link is issued.
/// This resolver only guarantees that a malformed or non-HTTPS value is never returned to a client.
/// Replace it behind <see cref="IBaeLinkResolver"/> if time-limited sharing links (Microsoft Graph) are needed later.
/// </summary>
public sealed class HttpsBaeLinkResolver : IBaeLinkResolver
{
    public string Resolve(string storedLink)
    {
        if (!Uri.TryCreate(storedLink, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            // Data-integrity fault: surfaced as a sanitized 5xx, the value itself is never logged.
            throw new InvalidOperationException("A stored document link is not an absolute HTTPS URL.");
        }

        return storedLink;
    }
}
