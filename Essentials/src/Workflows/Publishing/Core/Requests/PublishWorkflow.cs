using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Requests;

public sealed record PublishWorkflow(
    string VersionId,
    PublicationAction? Action = null,
    string? SlotName = null,
    string? ExpectedPublicationId = null,
    string? PreflightToken = null,
    string? TenantId = null) : IRequest<PublishedWorkflowView>;
