using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
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

internal static class PublicationSlotLifecycleEndpointHandler
{
    public static async Task HandleAsync<TRequest>(
        TRequest request,
        IRequestSender requestSender,
        IPublicationRecordStore publicationStore,
        ILogger logger,
        Func<PublicationSlotView, CancellationToken, Task> send,
        Action<(string Message, int StatusCode)> throwError,
        CancellationToken cancellationToken)
        where TRequest : IRequest<PublicationSlot>
    {
        try
        {
            var slot = await requestSender.Send(request, cancellationToken);
            var publication = await ListPublicationSlotsEndpoint.ResolveVisiblePublicationAsync(slot, publicationStore, cancellationToken);
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
