namespace Klesia.Application.Authorization;

public sealed class AccessDeniedException : Exception
{
    public AccessDeniedException()
        : base("Access denied.")
    {
    }
}
