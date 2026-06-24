using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;

namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record GetWorkflowInstance(string WorkflowExecutionId) : IRequest<GetWorkflowInstanceResponse>;

public sealed record GetWorkflowInstanceResponse(WorkflowInstanceDetailsView? Instance);
