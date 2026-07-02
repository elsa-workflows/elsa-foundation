namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Mutable per-dispatch workspace carried alongside the (immutable) pipeline context. Move 2 relocates a handler's
/// inline phases into their named slots; while a handler is still the pipeline's terminal it stages the results of a
/// phase here for the owning slot middleware to apply. The context record stays immutable — only this workspace mutates.
/// </summary>
public sealed class RuntimePipelineWorkspace
{
    /// <summary>
    /// A checkpoint commit assembled by the terminal handler for the <c>Checkpoint</c> slot to persist. The handler
    /// stages the commit instead of calling the committer inline; the Checkpoint middleware performs the actual
    /// <c>CommitAsync</c>. Null when the dispatch produced no checkpoint commit.
    /// </summary>
    public RuntimeCheckpointCommit? PendingCheckpointCommit { get; set; }
}
