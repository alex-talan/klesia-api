namespace Klesia.Application.Abstractions;

public interface IExtractionQueue
{
    public bool TryEnqueue(int demandId);
}