using System.Text.Json.Serialization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;

namespace Elsa.Workflows.Publishing.Api.Requests;

public sealed record PreflightActivityDraftPublication(
    [property: JsonIgnore] string DraftId,
    long ExpectedDraftRevision,
    string? ExpectedDefinitionHeadVersionId) : IRequest<ActivityPublicationPreflightView>;

public sealed record GetActivityPublicationReceipt(
    [property: JsonIgnore] string IdempotencyKey) : IRequest<ActivityPublicationReceiptView>;
