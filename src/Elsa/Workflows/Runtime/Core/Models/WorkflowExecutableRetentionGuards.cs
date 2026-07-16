namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Opaque provider-issued proof that a writer may establish a new durable retention root for an executable.
/// </summary>
/// <remarks>
/// Writers must acquire this lease before committing a source-reference or workflow-execution root. The provider
/// concurrency token prevents an expired or superseded lease from being renewed or released by a stale writer.
/// </remarks>
public sealed record WorkflowExecutableRootWriteLease(
    string ArtifactId,
    string LeaseId,
    string ConcurrencyToken);

/// <summary>
/// Opaque provider-issued guard reserving an executable for conditional garbage-collection deletion.
/// </summary>
/// <remarks>
/// While this guard is live, the provider refuses new root-write leases. The provider concurrency token makes the
/// final delete conditional on the exact guard that won the deletion race.
/// </remarks>
public sealed record WorkflowExecutableDeletionGuard(
    string ArtifactId,
    string OperationId,
    string ConcurrencyToken);
