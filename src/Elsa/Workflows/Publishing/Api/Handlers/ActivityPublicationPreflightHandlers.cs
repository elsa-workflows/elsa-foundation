using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;

namespace Elsa.Workflows.Publishing.Api.Handlers;

public sealed class PreflightActivityDraftPublicationRequestHandler(
    IActivityDefinitionPublisher publisher)
    : IRequestHandler<PreflightActivityDraftPublication, ActivityPublicationPreflightView>
{
    public Task<ActivityPublicationPreflightView> Handle(
        PreflightActivityDraftPublication request,
        CancellationToken cancellationToken) =>
        publisher.PreflightAsync(
            new(
                request.DraftId,
                request.ExpectedDraftRevision,
                request.ExpectedDefinitionHeadVersionId)
            {
                Version = request.Version
            },
            cancellationToken);
}

public sealed class GetActivityPublicationReceiptRequestHandler(
    IActivityDefinitionPublisher publisher)
    : IRequestHandler<GetActivityPublicationReceipt, ActivityPublicationReceiptView>
{
    public async Task<ActivityPublicationReceiptView> Handle(
        GetActivityPublicationReceipt request,
        CancellationToken cancellationToken) =>
        ActivityPublicationReceiptView.From(
            await publisher.GetReceiptAsync(request.IdempotencyKey, cancellationToken));
}
