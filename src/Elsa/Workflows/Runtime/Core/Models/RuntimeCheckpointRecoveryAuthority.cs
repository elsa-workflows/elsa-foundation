namespace Elsa.Workflows.Runtime.Core.Models;

public sealed record RuntimeCheckpointRecoveryAuthority(
    int Version,
    string Kind,
    string WorkflowExecutionId,
    string WorkItemId,
    string Fingerprint);
