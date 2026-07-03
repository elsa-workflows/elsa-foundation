using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Mutable per-dispatch workspace carried alongside the (immutable) pipeline context. Move 2 relocates a handler's
/// inline phases into their named slots; the handler runs in the <c>Invoke</c> slot and stages phase results here for
/// later slot middleware to apply. The context record stays immutable — only this workspace mutates.
/// </summary>
public sealed class RuntimePipelineWorkspace
{
    /// <summary>
    /// The invocation of the work item's selected handler, staged by the dispatcher for the <c>Invoke</c>-slot
    /// middleware to run in the before-<c>next</c> direction (so the handler executes before <c>Checkpoint</c>). The
    /// <c>Invoke</c> middleware clears this once it has run the handler; a non-null value surviving to the pipeline
    /// terminal means the <c>Invoke</c> slot was missing from the plan and is a hard error. Null when the handler runs
    /// as the pipeline terminal instead (e.g. the activity pipeline, not yet decomposed).
    /// </summary>
    public Func<IRuntimePipelineContext, ValueTask>? InvokeHandler { get; set; }

    /// <summary>
    /// The dispatch's cancellation token, staged so slot middleware (the pipeline delegate threads no token) can forward
    /// it to cancellable work such as the checkpoint commit.
    /// </summary>
    public CancellationToken CancellationToken { get; set; }

    private readonly List<RuntimeCheckpointCommit> _pendingCheckpointCommits = [];

    /// <summary>
    /// The ordered checkpoint commits a handler assembled in the <c>Invoke</c> slot for the <c>Checkpoint</c> slot to
    /// persist. The Checkpoint middleware drains this list <em>in stage order</em>, calling
    /// <c>RuntimeCheckpointCommitter.CommitAsync</c> once per entry — one staged entry maps to exactly one committer
    /// call, byte-identical to the handler's former inline sequence. Most handlers stage exactly one commit; handlers
    /// that commit multiple times per dispatch (e.g. InvokeActivity) stage several. The Checkpoint slot MUST NOT fold or
    /// batch these into a single commit: folding adjacent commits is the coalescing layer's job (W9), and batching at the
    /// slot would change W9 boundary detection and W5 fencing granularity.
    /// </summary>
    public IReadOnlyList<RuntimeCheckpointCommit> PendingCheckpointCommits => _pendingCheckpointCommits;

    /// <summary>Appends a checkpoint commit to the ordered staged list, preserving stage order for the Checkpoint slot.</summary>
    public void StageCheckpointCommit(RuntimeCheckpointCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        _pendingCheckpointCommits.Add(commit);
    }

    /// <summary>
    /// Convenience for the single-commit handlers (Cancel, Checkpoint): getting returns the sole staged commit (or null);
    /// setting clears the staged list and stages the given commit (or clears it when set to null). Handlers that stage
    /// more than one commit per dispatch use <see cref="StageCheckpointCommit"/> and <see cref="PendingCheckpointCommits"/>
    /// directly. Backed by the same ordered list the Checkpoint slot drains, so both paths persist identically.
    /// </summary>
    public RuntimeCheckpointCommit? PendingCheckpointCommit
    {
        get => _pendingCheckpointCommits.Count == 0 ? null : _pendingCheckpointCommits[^1];
        set
        {
            _pendingCheckpointCommits.Clear();
            if (value is not null)
                _pendingCheckpointCommits.Add(value);
        }
    }
}
