using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.Publications.Get;

/// <summary>
/// Reads one publication journal record by id, so a client holding a runtime activation slot's
/// <c>activeActivationId</c> can resolve the design version Publishing put behind that slot.
/// </summary>
/// <remarks>
/// Runtime owns "what is active" (<c>runtime/workflows/activation-slots/...</c>) and deliberately does not
/// join to publishing records; this route is the Publishing-owned other half of that join. An id that names
/// no record is a 404 rather than an empty success: an absent journal row is indistinguishable from a
/// misdirected read, and a silent null would let a client render "no design version" as a fact about the
/// slot instead of a failed lookup.
/// </remarks>
[Get("/publishing/publications/{publicationId}")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(IPublicationRecordStore publicationStore)
    : ApiEndpoint<GetPublicationRecord, PublicationView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "GetPublicationRecordEndpoint";
        options.Accepts = ["*/*", "application/json"];
    }

    public override async Task<PublicationView> HandleAsync(GetPublicationRecord request, CancellationToken cancellationToken)
    {
        // The route has no constraint, so a blank segment binds. It names no record, and answering it here keeps
        // the status independent of whether the configured store rejects or ignores a blank key.
        if (string.IsNullOrWhiteSpace(request.PublicationId))
            throw new EntityNotFoundException("Cannot read a publication: no publication id was named.");

        var publication = await publicationStore.FindAsync(request.PublicationId, cancellationToken)
            ?? throw new EntityNotFoundException($"Publication '{request.PublicationId}' was not found.");
        return PublicationView.From(publication);
    }
}
