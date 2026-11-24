using Klesia.Application.Demands;
using Klesia.Application.Authorization;
using Klesia.Infrastructure.Options;
using Klesia.WebApi;
using Klesia.WebApi.Authentication;
using Klesia.WebApi.Contracts;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Klesia.WebApi.Controllers;

[ApiController]
[Route("api/v1/demands")]
public sealed class DemandsController : ControllerBase
{
    private readonly DemandService _demandService;
    private readonly AuthorizedUserResolver _authorizedUserResolver;
    private readonly ICurrentPrincipal _currentPrincipal;
    private readonly ILogger<DemandsController> _logger;
    private readonly UploadOptions _uploadOptions;

    public DemandsController(
        DemandService demandService,
        AuthorizedUserResolver authorizedUserResolver,
        ICurrentPrincipal currentPrincipal,
        ILogger<DemandsController> logger,
        IOptions<UploadOptions> uploadOptions)
    {
        _demandService = demandService;
        _authorizedUserResolver = authorizedUserResolver;
        _currentPrincipal = currentPrincipal;
        _logger = logger;
        _uploadOptions = uploadOptions.Value;
    }

    [HttpGet]
    [ProducesResponseType<UserDemandsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDemandsResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var oid = _currentPrincipal.GetOid(User);
        var result = await _demandService.ListAsync(oid, cancellationToken);

        return Ok(UserDemandsResponse.From(result));
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<DemandResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreateAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        var maxPdfSizeBytes = _uploadOptions.MaxPdfSizeBytes;
        var requestLimit = UploadRequestLimits.ForPdfSize(maxPdfSizeBytes);
        var requestBodySizeFeature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (requestBodySizeFeature is { IsReadOnly: false })
        {
            requestBodySizeFeature.MaxRequestBodySize = requestLimit;
        }

        var oid = _currentPrincipal.GetOid(User);
        var user = await _authorizedUserResolver.ResolveAsync(oid, cancellationToken);

        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var contentType)
            || !string.Equals(contentType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return ProblemResponse(StatusCodes.Status415UnsupportedMediaType, "Unsupported Media Type");
        }

        DemandMultipartReadResult parsed;
        try
        {
            parsed = await DemandMultipartRequestReader.ReadAsync(Request, maxPdfSizeBytes, cancellationToken);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return ProblemResponse(StatusCodes.Status413PayloadTooLarge, "Payload Too Large");
        }
        catch (InvalidDataException)
        {
            return ProblemResponse(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["bae_file"] = ["The multipart request is malformed."]
                });
        }

        if (parsed is DemandMultipartReadResult.Invalid invalid)
        {
            return ProblemResponse(StatusCodes.Status400BadRequest, "Bad Request", invalid.Errors);
        }

        if (parsed is DemandMultipartReadResult.TooLarge)
        {
            return ProblemResponse(StatusCodes.Status413PayloadTooLarge, "Payload Too Large");
        }

        var input = ((DemandMultipartReadResult.Success)parsed).Input;
        await using var content = input.Content;
        var result = await _demandService.CreateAsync(user, input, cancellationToken);

        return result switch
        {
            CreateDemandResult.Created created => CreatedDemand(created.Demand),
            CreateDemandResult.Invalid invalidResult => ProblemResponse(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                invalidResult.Errors),
            CreateDemandResult.TooLarge => ProblemResponse(StatusCodes.Status413PayloadTooLarge, "Payload Too Large"),
            CreateDemandResult.Unprocessable unprocessable => ProblemResponse(
                StatusCodes.Status422UnprocessableEntity,
                "Unprocessable Content",
                unprocessable.Errors),
            CreateDemandResult.StorageUnavailable => ProblemResponse(
                StatusCodes.Status503ServiceUnavailable,
                "Service Unavailable"),
            _ => throw new InvalidOperationException("Unknown demand creation result.")
        };
    }

    [HttpGet("{demand_id:int:min(1)}/bae")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDocumentAsync(
        [FromRoute(Name = "demand_id")] int demandId,
        CancellationToken cancellationToken)
    {
        var oid = _currentPrincipal.GetOid(User);
        var document = await _demandService.OpenDocumentAsync(oid, demandId, cancellationToken);

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/pdf";
        Response.Headers["Content-Disposition"] = $"inline; filename=\"bae-{demandId}.pdf\"";
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (document.Length is not null)
        {
            Response.ContentLength = document.Length;
        }

        try
        {
            await document.Content.CopyToAsync(Response.Body, 81920, HttpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            if (Response.HasStarted)
            {
                _logger.LogWarning("Document stream canceled for demand {DemandId}.", demandId);
                HttpContext.Abort();
            }
        }
        catch (Exception) when (Response.HasStarted)
        {
            _logger.LogError("Document stream failed after response start for demand {DemandId}.", demandId);
            HttpContext.Abort();
        }
        finally
        {
            await document.Content.DisposeAsync();
        }

        return new EmptyResult();
    }

    private IActionResult CreatedDemand(Domain.Demands.Demand demand)
    {
        var response = DemandResponse.From(new DemandListItem(
            demand.Id,
            demand.GKNumber.Value,
            demand.Status.Code));

        return StatusCode(StatusCodes.Status201Created, response);
    }

    private ObjectResult ProblemResponse(
        int status,
        string title,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title
        };
        var traceId = HttpContext.Items.TryGetValue(
            Klesia.WebApi.Logging.CorrelationIdMiddleware.ItemName,
            out var value)
            ? value?.ToString()
            : HttpContext.TraceIdentifier;
        problem.Extensions["traceId"] = traceId;
        if (errors is not null)
        {
            problem.Extensions["errors"] = errors;
        }

        var result = new ObjectResult(problem)
        {
            StatusCode = status
        };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}