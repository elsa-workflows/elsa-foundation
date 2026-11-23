namespace Elsa.Workflows.Runtime.Api.Requests;

// Spec 1229 (S-6): the variable lifecycle inspection requests (contracts/inspection-api.md).

public sealed record ListInstanceVariables(string WorkflowExecutionId);

public sealed record ListVariableWrites(
    string WorkflowExecutionId,
    string? FrameId = null,
    string? ScopeId = null,
    string? VariableKey = null,
    string? Source = null,
    string? Outcome = null,
    bool? Overwrites = null,
    string? Cursor = null,
    int? Take = null);

public sealed record GetInstanceJournal(string WorkflowExecutionId, string? Cursor = null, int? Take = null);

public sealed record CompareWorkflowInstances(string WorkflowExecutionId, string OtherWorkflowExecutionId);

public sealed record GetVariableWritePayload(string WorkflowExecutionId, string EntryId, string? Value = null);
