using Klesia.Application.Abstractions;
using Klesia.Application.Authorization;
using Klesia.Application.Documents;

namespace Klesia.Application.Demands;

public sealed class DemandService
{
    private readonly AuthorizedUserResolver _authorizedUserResolver;
    private readonly IDemandRepository _demandRepository;
    private readonly IBaeDocumentStore _documentStore;

    public DemandService(
        AuthorizedUserResolver authorizedUserResolver,
        IDemandRepository demandRepository,
        IBaeDocumentStore documentStore)
    {
        _authorizedUserResolver = authorizedUserResolver;
        _demandRepository = demandRepository;
        _documentStore = documentStore;
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
}