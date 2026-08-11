using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Scoped, explicitly-threaded transport for the current dispatch's claimed scheduler work item (WU-1 / spec 105). The
/// drainer opens a scope for the claimed item before dispatch; the checkpoint committer reads the pending claim to fold
/// its fence-checked delete into the commit, and reports back through <see cref="MarkConsumedDurably"/> when the store
/// durably consumed it. After dispatch the drainer reads <see cref="WasConsumedDurably"/> to decide whether the separate
/// acknowledgement can be skipped.
/// </summary>
/// <remarks>
/// This is an explicit scoped DI collaborator shared by the same-scope drainer and committer, not an AsyncLocal service
/// locator (RT-7): the drain already threads its ambient services explicitly, and this accessor rides that thread.
/// </remarks>
public interface IRuntimeConsumedSchedulerWorkClaimAccessor
{
    /// <summary>The claimed work item to consume during the current dispatch, or <see langword="null"/> when none.</summary>
    ConsumedSchedulerWorkItem? PendingConsume { get; }

    /// <summary>Whether a checkpoint commit during the current dispatch durably consumed the pending claim.</summary>
    bool WasConsumedDurably { get; }

    /// <summary>Opens a dispatch scope carrying the claimed item; dispose the returned handle to clear it.</summary>
    IDisposable Begin(ConsumedSchedulerWorkItem consume);

    /// <summary>Records that the store durably deleted the pending claim's work item inside a checkpoint commit.</summary>
    void MarkConsumedDurably(string workItemId);

    /// <summary>
    /// Opens a consume attempt for <paramref name="workItemId"/> around the store call that carries its deletion;
    /// dispose the returned handle when the attempt settles either way. Between those two points the item may already
    /// be gone from the queue while <see cref="MarkConsumedDurably"/> has not run yet, so
    /// <see cref="IsConsumeInFlightOrDurable"/> reports the attempt as the explanation for a vanished claim (#1254).
    /// </summary>
    IDisposable BeginConsumeAttempt(string workItemId);

    /// <summary>
    /// Whether this dispatch's own consume of <paramref name="workItemId"/> is in flight or has landed durably. Read
    /// by the drainer's claim-renewal loop, which runs on its own task while the dispatch commits, so implementations
    /// must be safe to read concurrently with the dispatch thread's writes.
    /// </summary>
    bool IsConsumeInFlightOrDurable(string workItemId);
}
