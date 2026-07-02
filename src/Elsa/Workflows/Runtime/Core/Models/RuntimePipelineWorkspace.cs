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
    /// middleware to run in the before-<c>next</c> direction (so the handler executes before <c>Checkpoint</c>). Null
    /// when the handler runs as the pipeline terminal instead (e.g. the activity pipeline, not yet decomposed).
    /// </summary>
    public Func<IRuntimePipelineContext, ValueTask>? InvokeHandler { get; set; }

    /// <summary>
    /// A checkpoint commit assembled by the handler in the <c>Invoke</c> slot for the <c>Checkpoint</c> slot to persist.
    /// The handler stages the commit instead of committing inline; the Checkpoint middleware performs the actual
    /// <c>CommitAsync</c> before <c>next</c>. Null when the dispatch produced no checkpoint commit.
    /// </summary>
    public RuntimeCheckpointCommit? PendingCheckpointCommit { get; set; }
}
