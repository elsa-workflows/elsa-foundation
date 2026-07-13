using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Requests;

public sealed record PublishWorkflow(
    string VersionId,
    PublicationAction? Action = null,
    string? SlotName = null,
    string? ExpectedPublicationId = null) : IRequest<PublishedWorkflowView>;
