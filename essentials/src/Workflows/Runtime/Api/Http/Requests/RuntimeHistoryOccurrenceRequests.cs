namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record GetRuntimeHistoryOccurrences(string WorkflowExecutionId, int Limit = 50,
    string? SequenceToken = null, long? Position = null, string? AnchorActivityExecutionId = null,
    string? ActivityExecutionId = null, bool IncludeDescendants = false);
