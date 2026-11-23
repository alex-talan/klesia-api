namespace Klesia.Infrastructure.Extraction;

internal interface IExtractionQueueReader
{
    public IAsyncEnumerable<int> ReadAllAsync(CancellationToken cancellationToken);
}