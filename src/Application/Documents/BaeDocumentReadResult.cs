namespace Klesia.Application.Documents;

public sealed record BaeDocumentReadResult(Stream Content, long? Length);