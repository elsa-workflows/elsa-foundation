namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record BrowseActivityExecutions(string WorkflowExecutionId, string? ExecutableNodeId = null,
    string? ParentActivityExecutionId = null, string? AncestorActivityExecutionId = null,
    int Limit = 50, string? Cursor = null, string? AnchorActivityExecutionId = null);
