using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Provider-owned bounded read projection of the same history written atomically with runtime checkpoints.</summary>
public interface IRuntimeHistoryStore
{
    ValueTask<RuntimeHistoryHead?> FindHeadAsync(string workflowExecutionId, CancellationToken cancellationToken = default);
    ValueTask<RuntimeHistoryPage> ReadPageAsync(RuntimeHistoryQuery query, CancellationToken cancellationToken = default);
    ValueTask<RuntimeHistoryEvent?> FindEventAsync(string workflowExecutionId, string historyEventId, CancellationToken cancellationToken = default);
    ValueTask<RuntimeHistoryBookmarkPage> ListBookmarksAsync(RuntimeHistoryBookmarkQuery query, CancellationToken cancellationToken = default);
    ValueTask<RuntimeHistoryBookmark?> FindBookmarkAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default);
}

/// <summary>Records delivery observations at their own durable authority, without asserting execution effects.</summary>
public interface IRuntimeHistoryObservationWriter
{
    ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default);
}

/// <summary>Explicit owning retention mutation; inspection never calls this port. Default retention follows the run.</summary>
public interface IRuntimeHistoryRetentionStore
{
    /// <summary>Atomically purges event content through a committed position and returns the actual purged prefix.
    /// Retains policy, allocation and semantic replay receipts until run deletion. Missing history returns zero.</summary>
    ValueTask<long> PurgeThroughAsync(string workflowExecutionId, long throughSequence, CancellationToken cancellationToken = default);
}
