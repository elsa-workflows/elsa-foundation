using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record GetWorkflowInstance(
    string WorkflowExecutionId,
    int ActivityPageSize = RuntimeStorePageRequest.DefaultLimit,
    string? ActivityContinuationToken = null) : IRequest<GetWorkflowInstanceResponse>;

public sealed record GetWorkflowInstanceResponse(WorkflowInstanceDetailsView? Instance);
