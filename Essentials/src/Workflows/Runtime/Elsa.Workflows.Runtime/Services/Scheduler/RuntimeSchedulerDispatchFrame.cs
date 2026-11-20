using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Scheduler;

/// <summary>
/// One claimed scheduler dispatch on the current async flow, and whether it has sent a non-retaining checkpoint of its
/// workflow execution to the commit store (#3275). <see cref="WorkflowSchedulerDrainer"/> opens one around each dispatch
/// it could release with a delay, for a <c>RetryAfter</c> or for temporary unavailability (#3683), and
/// <see cref="Checkpoints.RuntimeCheckpointCommitter"/> records the attempt just before its store call, whatever the store
/// then does with it: a coalescing segment buffers, folds or passes it through, and a store can land it and still throw.
/// The drainer releases the item only when no attempt was recorded, so the decision never rests on inferring afterwards
/// whether a faulted commit left a durable effect. The one exception is a store that refuses the commit itself with
/// <see cref="Core.Exceptions.RuntimeSchedulerWorkUnavailableException"/>, rolling it back: that commit was not applied,
/// so the committer withdraws its own attempt (#3683), and the dispatch stays marked only for its other attempts.
/// </summary>
/// <remarks>
/// The frame lives on the async flow rather than in the drain's DI scope because a handler may commit through a
/// committer it resolves from a fresh scope of its own, as <c>WorkflowResumeBookmarkSchedulerWorkHandler</c> does. Frames
/// nest. An attempt marks every open frame for the commit's execution, so work dispatched inline inside a dispatch (the
/// fused completion pump, a nested drain of the same execution) counts against the dispatch it ran inside, while a nested
/// drain of another execution marks only its own frame. Disposing a frame makes the frame it replaced current again.
/// A frame counts attempts rather than flagging them, so withdrawing one refused commit leaves the frame marked by any
/// other non-retaining commit of the same dispatch, an earlier buffered one that a later flush still persists included,
/// whatever order concurrent commits finish in.
/// </remarks>
internal sealed class RuntimeSchedulerDispatchFrame : IDisposable
{
    private static readonly AsyncLocal<RuntimeSchedulerDispatchFrame?> Ambient = new();

    private readonly string _workflowExecutionId;
    private readonly RuntimeSchedulerDispatchFrame? _prior;
    private int _nonRetainingCheckpointAttempts;
    private bool _disposed;

    private RuntimeSchedulerDispatchFrame(string workflowExecutionId, RuntimeSchedulerDispatchFrame? prior)
    {
        _workflowExecutionId = workflowExecutionId;
        _prior = prior;
    }

    /// <summary>Whether a non-retaining checkpoint of this frame's execution went to the commit store while it was open.</summary>
    public bool NonRetainingCheckpointAttempted => Volatile.Read(ref _nonRetainingCheckpointAttempts) > 0;

    public static RuntimeSchedulerDispatchFrame Open(string workflowExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        var frame = new RuntimeSchedulerDispatchFrame(workflowExecutionId, Ambient.Value);
        Ambient.Value = frame;
        return frame;
    }

    // A retaining checkpoint keeps the item queued for redrive by design, so running it again afterwards is the intended
    // outcome rather than a repeat, and it is not recorded.
    public static CheckpointAttempt RecordCheckpointAttempt(RuntimeCheckpointCommit commit)
    {
        if (commit.RetainSchedulerWork)
            return default;
        List<RuntimeSchedulerDispatchFrame>? marked = null;
        for (var frame = Ambient.Value; frame is not null; frame = frame._prior)
        {
            if (!StringComparer.Ordinal.Equals(frame._workflowExecutionId, commit.WorkflowExecutionId))
                continue;
            Interlocked.Increment(ref frame._nonRetainingCheckpointAttempts);
            (marked ??= []).Add(frame);
        }

        return new CheckpointAttempt(marked);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Ambient.Value = _prior;
        _disposed = true;
    }

    /// <summary>One recorded attempt and the frames it marked, so that this commit alone can be withdrawn.</summary>
    internal readonly struct CheckpointAttempt
    {
        private readonly IReadOnlyList<RuntimeSchedulerDispatchFrame>? _frames;

        internal CheckpointAttempt(IReadOnlyList<RuntimeSchedulerDispatchFrame>? frames) => _frames = frames;

        /// <summary>
        /// Takes back this attempt only. Called once, when the store refused the commit as unavailable without applying
        /// it; every other attempt recorded on the same frames still counts.
        /// </summary>
        public void Withdraw()
        {
            if (_frames is null)
                return;
            foreach (var frame in _frames)
                Interlocked.Decrement(ref frame._nonRetainingCheckpointAttempts);
        }
    }
}
