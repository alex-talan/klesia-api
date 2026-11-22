using Microsoft.AspNetCore.Mvc;

namespace Klesia.WebApi.Errors;

public static class Problems
{
    public static IResult BadRequest(IReadOnlyDictionary<string, string[]>? errors = null) =>
        Problem(StatusCodes.Status400BadRequest, "Bad Request", detail: null, errors);

    public static IResult UnprocessableContent(IReadOnlyDictionary<string, string[]>? errors = null) =>
        Problem(StatusCodes.Status422UnprocessableEntity, "Unprocessable Content", detail: null, errors);

    public static IResult NotFound() =>
        Problem(StatusCodes.Status404NotFound, "Not Found");

    public static IResult PayloadTooLarge() =>
        Problem(StatusCodes.Status413PayloadTooLarge, "Payload Too Large");

    public static IResult UnsupportedMediaType() =>
        Problem(StatusCodes.Status415UnsupportedMediaType, "Unsupported Media Type");

    public static IResult ServiceUnavailable() =>
        Problem(StatusCodes.Status503ServiceUnavailable, "Service Unavailable");

    public static IResult Forbidden() =>
        Problem(StatusCodes.Status403Forbidden, "Forbidden");

    public static async Task WriteForbiddenAsync(HttpContext context, string? traceId)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await Results.Json(new
        {
            title = "Forbidden",
            status = StatusCodes.Status403Forbidden,
            traceId
        }, contentType: "application/problem+json", statusCode: StatusCodes.Status403Forbidden)
            .ExecuteAsync(context);
    }

    public static IResult InternalServerError() =>
        Problem(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");

    private static IResult Problem(
        int status,
        string title,
        string? detail = null,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail
        };

        if (errors is not null)
        {
            problem.Extensions["errors"] = errors;
        }

        return Results.Json(problem, statusCode: status, contentType: "application/problem+json");
    }
}
