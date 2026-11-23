namespace Klesia.Application.Documents;

public sealed class DocumentStoreUnavailableException : Exception
{
    public DocumentStoreUnavailableException()
        : base("Document storage is unavailable.")
    {
    }
}