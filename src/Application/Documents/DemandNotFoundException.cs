namespace Klesia.Application.Documents;

public sealed class DemandNotFoundException : Exception
{
    public DemandNotFoundException()
        : base("The requested demand was not found.")
    {
    }
}