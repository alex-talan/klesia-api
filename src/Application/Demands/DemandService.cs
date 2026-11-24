using Klesia.Application.Abstractions;
using Klesia.Application.Authorization;
using Klesia.Application.Documents;
using Klesia.Domain.Users;
using Microsoft.Extensions.Logging;

namespace Klesia.Application.Demands;

public sealed class DemandService
{
    private readonly AuthorizedUserResolver _authorizedUserResolver;
    private readonly IDemandRepository _demandRepository;
    private readonly IBaeDocumentStore _documentStore;
    private readonly INotifier _notifier;
    private readonly IExtractionQueue _extractionQueue;
    private readonly CreateDemandValidator _createDemandValidator;
    private readonly IDemandCreationSettings _creationSettings;
    private readonly ILogger<DemandService> _logger;

    public DemandService(
        AuthorizedUserResolver authorizedUserResolver,
        IDemandRepository demandRepository,
        IBaeDocumentStore documentStore,
        INotifier notifier,
        IExtractionQueue extractionQueue,
        CreateDemandValidator createDemandValidator,
        IDemandCreationSettings creationSettings,
        ILogger<DemandService> logger)
    {
        ArgumentNullException.ThrowIfNull(authorizedUserResolver);
        ArgumentNullException.ThrowIfNull(demandRepository);
        ArgumentNullException.ThrowIfNull(documentStore);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(extractionQueue);
        ArgumentNullException.ThrowIfNull(createDemandValidator);
        ArgumentNullException.ThrowIfNull(creationSettings);
        ArgumentNullException.ThrowIfNull(logger);

        _authorizedUserResolver = authorizedUserResolver;
        _demandRepository = demandRepository;
        _documentStore = documentStore;
        _notifier = notifier;
        _extractionQueue = extractionQueue;
        _createDemandValidator = createDemandValidator;
        _creationSettings = creationSettings;
        _logger = logger;
    }

    public async Task<UserDemandsResult> ListAsync(Guid oid, CancellationToken cancellationToken)
    {
        var user = await _authorizedUserResolver.ResolveAsync(oid, cancellationToken);
        var demands = await _demandRepository.ListByUserAsync(user.Id, cancellationToken);

        var items = demands
            .Select(demand => new DemandListItem(
                demand.Id,
                demand.GKNumber.Value,
                demand.Status.Code))
            .ToArray();

        return new UserDemandsResult(user.UserUniqueId, user.Name, items);
    }

    public async Task<BaeDocumentReadResult> OpenDocumentAsync(
        Guid oid,
        int demandId,
        CancellationToken cancellationToken)
    {
        var user = await _authorizedUserResolver.ResolveAsync(oid, cancellationToken);
        var blobUrlValue = await _demandRepository.GetOwnedBaeLinkAsync(
            demandId,
            user.Id,
            cancellationToken);

        if (blobUrlValue is null)
        {
            throw new DemandNotFoundException();
        }

        if (!Uri.TryCreate(blobUrlValue, UriKind.Absolute, out var blobUrl)
            || !_documentStore.IsOwnedLocation(blobUrl))
        {
            throw new StoredDocumentIntegrityException(demandId);
        }

        try
        {
            return await _documentStore.OpenReadAsync(blobUrl, cancellationToken);
        }
        catch (DocumentNotFoundException)
        {
            throw new StoredDocumentIntegrityException(demandId);
        }
    }

    public async Task<CreateDemandResult> CreateAsync(
        Guid oid,
        CreateDemandInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var user = await _authorizedUserResolver.ResolveAsync(oid, cancellationToken);
        return await CreateAsync(user, input, cancellationToken);
    }

    public async Task<CreateDemandResult> CreateAsync(
        ApplicationUser user,
        CreateDemandInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(input);

        var validation = await _createDemandValidator.ValidateAsync(
            new CreateDemandValidationInput(
                input.GkNumber,
                input.DeclaredContentType,
                input.Length,
                input.Content),
            _creationSettings.MaxPdfSizeBytes,
            cancellationToken);

        if (validation is CreateDemandValidationResult.Invalid invalid)
        {
            return new CreateDemandResult.Invalid(invalid.Errors);
        }

        if (validation is CreateDemandValidationResult.TooLarge)
        {
            return new CreateDemandResult.TooLarge();
        }

        if (validation is CreateDemandValidationResult.Unprocessable unprocessable)
        {
            return new CreateDemandResult.Unprocessable(unprocessable.Errors);
        }

        var valid = (CreateDemandValidationResult.Valid)validation;
        Uri blobUrl;
        try
        {
            blobUrl = await _documentStore.UploadAsync(
                user.UserUniqueId,
                valid.PdfContent,
                cancellationToken);
        }
        catch (DocumentStoreUnavailableException)
        {
            return new CreateDemandResult.StorageUnavailable();
        }

        var gkNumber = Domain.Demands.GKNumber.Create(input.GkNumber);
        int demandId;
        try
        {
            demandId = await _demandRepository.CreatePendingAsync(
                user.Id,
                gkNumber,
                blobUrl,
                cancellationToken);
        }
        catch
        {
            try
            {
                await _documentStore.DeleteAsync(blobUrl, CancellationToken.None);
            }
            catch (Exception)
            {
                _logger.LogWarning("Best-effort BAE cleanup failed after demand insert failure.");
            }

            throw;
        }

        try
        {
            await _notifier.NotifyDemandsChangedAsync(user.UserUniqueId, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Demand change notification failed after demand commit.");
        }

        try
        {
            _ = _extractionQueue.TryEnqueue(demandId);
        }
        catch (Exception)
        {
            _logger.LogWarning(
                "Extraction queue enqueue failed; demand will be recovered by the sweep. DemandId: {DemandId}",
                demandId);
        }

        return new CreateDemandResult.Created(new Domain.Demands.Demand(
            demandId,
            user.Id,
            gkNumber,
            Domain.Demands.DemandStatus.Pending));
    }
}