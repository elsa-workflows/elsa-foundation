using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;

namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record ListWorkflowDispatches(
    string? ParentWorkflowExecutionId,
    string? ChildWorkflowExecutionId,
    string? Status,
    int? Take = null) : IRequest<IReadOnlyCollection<WorkflowDispatchView>>
{
    public DateTimeOffset? AfterCreatedAt { get; init; }
    public string? AfterDispatchId { get; init; }
}

public sealed record GetWorkflowDispatch(string DispatchId);

/// <summary>The route supplies DispatchId; callers supply only a stable idempotency RequestId.</summary>
public sealed record RedriveWorkflowDispatch(string DispatchId, string RequestId) : IRequest<WorkflowDispatchRedriveView>;
