namespace Application.Common.Exceptions;

/// <summary>
/// The caller is authenticated but is not allowed to use the application.
/// The message is deliberately generic: it must never reveal whether the account is unknown or disabled.
/// </summary>
public sealed class ForbiddenException() : Exception("Access denied.")
{
}
