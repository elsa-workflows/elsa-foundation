using Elsa.Workflows.Publishing.Api.Models;
using System.Text.Json.Serialization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Workflows.Publishing.Api.Requests;

public sealed record PublishActivityDraft(
    [property: JsonIgnore] string DraftId,
    long ExpectedDraftRevision,
    string? ExpectedDefinitionHeadVersionId,
    string Version,
    string ReviewToken,
    string IdempotencyKey) : IRequest<ActivityPublicationReceiptView>;
