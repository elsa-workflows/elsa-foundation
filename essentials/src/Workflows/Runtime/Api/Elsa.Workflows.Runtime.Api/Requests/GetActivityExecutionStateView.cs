namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record GetActivityExecutionStateView(string WorkflowExecutionId, string ActivityExecutionId);
