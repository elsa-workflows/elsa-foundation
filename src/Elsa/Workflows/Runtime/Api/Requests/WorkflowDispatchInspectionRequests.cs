using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;

namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record ListWorkflowDispatches(
    string? ParentWorkflowExecutionId,
    string? ChildWorkflowExecutionId,
    string? Status,
    int? Take = null) : IRequest<IReadOnlyCollection<WorkflowDispatchView>>;

public sealed record GetWorkflowDispatch(string DispatchId) : IRequest<GetWorkflowDispatchResponse>;

public sealed record GetWorkflowDispatchResponse(WorkflowDispatchView? Dispatch);
