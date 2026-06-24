using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;

namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record GetActivityExecution(string WorkflowExecutionId, string ActivityExecutionId) : IRequest<GetActivityExecutionResponse>;

public sealed record GetActivityExecutionResponse(ActivityExecutionInspectionView? ActivityExecution);
