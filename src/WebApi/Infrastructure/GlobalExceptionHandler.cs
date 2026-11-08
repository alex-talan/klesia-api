using Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Infrastructure;

/// <summary>
/// Maps exceptions to safe ProblemDetails responses. Bodies never contain stack traces, SQL, tokens or
/// connection strings; the 403 body is identical for unknown and disabled accounts.
/// </summary>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        (int statusCode, string title) = exception switch
        {
            ForbiddenException => (StatusCodes.Status403Forbidden, "Access denied."),
            NotFoundException => (StatusCodes.Status404NotFound, "The requested resource was not found."),
            ArgumentException => (StatusCodes.Status400BadRequest, "The request is invalid."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception mapped to {StatusCode}", statusCode);
        }
        else
        {
            logger.LogWarning("Request rejected with {StatusCode}", statusCode);
        }

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title
            }
        });
    }
}
