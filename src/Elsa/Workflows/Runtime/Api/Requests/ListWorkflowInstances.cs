using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;

namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record ListWorkflowInstances(
    string? Status,
    string? DefinitionId,
    string? CorrelationId,
    int? Take,
    string? Cursor = null,
    string? WorkflowExecutionId = null,
    string? ArtifactId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? RunKind = null)
    : IRequest<WorkflowInstanceListView>;
