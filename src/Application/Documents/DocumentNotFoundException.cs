namespace Klesia.Application.Documents;

public sealed class DocumentNotFoundException : Exception
{
    public DocumentNotFoundException()
        : base("The stored document was not found.")
    {
    }
}