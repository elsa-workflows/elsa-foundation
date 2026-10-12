using Elsa.Workflows.Publishing.Api.Models;
using System.Text.Json.Serialization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Workflows.Publishing.Api.Requests;

public sealed record PreflightActivityDraftPublication(
    [property: JsonIgnore] string DraftId,
    long ExpectedDraftRevision,
    string? ExpectedDefinitionHeadVersionId) : IRequest<ActivityPublicationPreflightView>
{
    public string? Version { get; init; }
}

public sealed record GetActivityPublicationReceipt(
    [property: JsonIgnore] string IdempotencyKey) : IRequest<ActivityPublicationReceiptView>;
