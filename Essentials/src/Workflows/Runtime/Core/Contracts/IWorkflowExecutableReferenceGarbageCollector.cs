using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// The ADR 0040 reference garbage collector: a two-query sweep that first drops expired/retired
/// <see cref="WorkflowExecutableSourceReference"/> records, then deletes the content-addressed artifacts no live
/// reference points at any more (dangling-image pruning). Artifact lifetime is derived, never stored — an artifact
/// is retained precisely while a live reference points at it.
/// </summary>
public interface IWorkflowExecutableReferenceGarbageCollector
{
    /// <summary>Runs one sweep as of the current time and reports what it removed.</summary>
    ValueTask<WorkflowExecutableReferenceSweepResult> SweepAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs one sweep as of <paramref name="now"/> (deterministic entry point for tests and hosts driving their own clock).</summary>
    ValueTask<WorkflowExecutableReferenceSweepResult> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
