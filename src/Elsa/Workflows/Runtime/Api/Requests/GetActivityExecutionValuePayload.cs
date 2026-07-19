using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Contracts;

namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record GetActivityExecutionValuePayload(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string EvidenceId) : IRequest<GetActivityExecutionValuePayloadResponse>;

public sealed record GetActivityExecutionValuePayloadResponse(ActivityExecutionValuePayloadReadResult Result);
