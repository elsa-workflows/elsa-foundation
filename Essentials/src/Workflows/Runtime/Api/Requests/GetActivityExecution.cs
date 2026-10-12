namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record GetActivityExecution(string WorkflowExecutionId, string ActivityExecutionId);
