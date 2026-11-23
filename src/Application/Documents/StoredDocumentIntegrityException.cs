namespace Klesia.Application.Documents;

public sealed class StoredDocumentIntegrityException : Exception
{
    public StoredDocumentIntegrityException(int demandId)
        : base("The stored document for the demand is invalid or missing.")
    {
        DemandId = demandId;
    }

    public int DemandId { get; }
}