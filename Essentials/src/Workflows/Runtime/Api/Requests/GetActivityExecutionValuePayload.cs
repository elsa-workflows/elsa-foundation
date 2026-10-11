namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record GetActivityExecutionValuePayload(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string EvidenceId);
