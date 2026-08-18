using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Handlers;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

internal sealed class UnpublishPublicationSlotEndpoint(
    IRequestSender requestSender,
    IPublicationRecordStore publicationStore,
    ILogger<UnpublishPublicationSlotEndpoint> logger)
    : ElsaEndpoint<UnpublishPublicationSlotRequest, PublicationSlotView>
{
    public override void Configure()
    {
        Delete(RouteConstants.WorkflowSlot);
        ConfigurePermissions(PermissionNames.WorkflowPublishingManage);
    }

    public override Task HandleAsync(UnpublishPublicationSlotRequest request, CancellationToken cancellationToken) =>
        PublicationSlotLifecycleEndpointHandler.HandleAsync(
            new UnpublishPublicationSlot(request.DefinitionId, request.SlotName),
            requestSender,
            publicationStore,
            logger,
            (response, ct) => Send.OkAsync(response, ct),
            error => ThrowError(error.Message, error.StatusCode),
            cancellationToken);
}

internal sealed class RestorePublicationSlotEndpoint(
    IRequestSender requestSender,
    IPublicationRecordStore publicationStore,
    ILogger<RestorePublicationSlotEndpoint> logger)
    : ElsaEndpoint<RestorePublicationSlotRequest, PublicationSlotView>
{
    public override void Configure()
    {
        Post(RouteConstants.WorkflowSlotRestore);
        ConfigurePermissions(PermissionNames.WorkflowPublishingManage);
    }

    public override Task HandleAsync(RestorePublicationSlotRequest request, CancellationToken cancellationToken) =>
        PublicationSlotLifecycleEndpointHandler.HandleAsync(
            new RestorePublicationSlot(request.DefinitionId, request.SlotName),
            requestSender,
            publicationStore,
            logger,
            (response, ct) => Send.OkAsync(response, ct),
            error => ThrowError(error.Message, error.StatusCode),
            cancellationToken);
}

/// <summary>
/// The publishing half of a slot lifecycle command: retire or restore a publication, then render the resulting
/// activation slot together with the <see cref="PublicationRecord"/> that explains it.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are commands, not reads (T117).</b> The slot <em>listing</em> moved to
/// <c>Elsa.Workflows.Runtime.Api</c> because the slot is a runtime concept; unpublishing and restoring stayed
/// here because retracting or reinstating a publication is a publishing command that happens to ask the runtime
/// authority to change the ledger. What survives here is the enrichment: joining a slot to its optional
/// publication is a publishing concern, and only publishing holds the journal to do it.
/// </para>
/// </remarks>
internal static class PublicationSlotLifecycleEndpointHandler
{
    /// <summary>
    /// The publication a slot should be rendered with, or <see langword="null"/> when there is none.
    /// </summary>
    /// <remarks>
    /// Absence is a normal answer, never an error: it means "not published by me". A slot activated by artifact
    /// reconciliation has no publication record at all, and a runtime-only engine has no journal to look in.
    /// </remarks>
    internal static async ValueTask<PublicationRecord?> ResolveVisiblePublicationAsync(
        WorkflowActivationSlot slot,
        IPublicationRecordStore publicationStore,
        CancellationToken cancellationToken)
    {
        if (slot.ActiveActivationId is { } activePublicationId)
            return await publicationStore.FindAsync(activePublicationId, cancellationToken);

        return (await publicationStore.ListBySlotAsync(slot.SlotId, cancellationToken))
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.PublicationId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static async Task HandleAsync<TRequest>(
        TRequest request,
        IRequestSender requestSender,
        IPublicationRecordStore publicationStore,
        ILogger logger,
        Func<PublicationSlotView, CancellationToken, Task> send,
        Action<(string Message, int StatusCode)> throwError,
        CancellationToken cancellationToken)
        where TRequest : IRequest<WorkflowActivationSlot>
    {
        try
        {
            var slot = await requestSender.Send(request, cancellationToken);
            var publication = await ResolveVisiblePublicationAsync(slot, publicationStore, cancellationToken);
            await send(PublicationSlotView.From(slot, publication), cancellationToken);
        }
        catch (PublicationActivationException exception)
        {
            throwError((exception.Message, 409));
        }
        catch (InvalidOperationException exception) when (IsMissing(exception))
        {
            throwError((exception.Message, 404));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected publication slot lifecycle failure for request '{RequestType}'.", typeof(TRequest));
            throwError(("Unexpected error occurred", 500));
        }
    }

    private static bool IsMissing(InvalidOperationException exception) =>
        exception.Message.Contains("does not exist", StringComparison.Ordinal) ||
        exception.Message.Contains("no retired publication", StringComparison.Ordinal) ||
        exception.Message.Contains("unavailable", StringComparison.Ordinal) ||
        exception.Message.Contains("no source reference", StringComparison.Ordinal);
}
