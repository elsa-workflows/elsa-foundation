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
    : IRequest<WorkflowInstanceListView>
{
    internal WorkflowInstanceListPagingContract PagingContract { get; private init; } = WorkflowInstanceListPagingContract.Paged;

    /// <summary>
    /// Selects the historical array route's paging bounds without adding an HTTP-bound request field. The legacy
    /// route calls this before mediation; the additive page route uses the bounded default contract.
    /// </summary>
    public ListWorkflowInstances ForLegacyArray() => this with { PagingContract = WorkflowInstanceListPagingContract.LegacyArray };
}

internal enum WorkflowInstanceListPagingContract
{
    Paged,
    LegacyArray
}
