namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record GetActivityExecutionDescendants(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string? Cursor = null,
    int? Limit = null,
    string? Include = null);

public sealed record GetActivityExecutionLayout(string WorkflowExecutionId, string ActivityExecutionId);
