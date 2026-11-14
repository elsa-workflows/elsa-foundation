using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Provider-owned bounded read projection of the same history written atomically with runtime checkpoints.</summary>
public interface IRuntimeHistoryStore
{
    /// <exception cref="RuntimeHistoryUnavailableException">A normalized provider failure retains its original cause and localizing identifiers.</exception>
    ValueTask<RuntimeHistoryHead?> FindHeadAsync(string workflowExecutionId, CancellationToken cancellationToken = default);
    /// <exception cref="RuntimeHistoryUnavailableException">A normalized provider failure retains its original cause and localizing identifiers.</exception>
    ValueTask<RuntimeHistoryPage> ReadPageAsync(RuntimeHistoryQuery query, CancellationToken cancellationToken = default);
    /// <exception cref="RuntimeHistoryUnavailableException">A normalized provider failure retains its original cause and localizing identifiers.</exception>
    ValueTask<RuntimeHistoryEvent?> FindEventAsync(string workflowExecutionId, string historyEventId, CancellationToken cancellationToken = default);
    /// <exception cref="RuntimeHistoryUnavailableException">A normalized provider failure retains its original cause and localizing identifiers.</exception>
    ValueTask<RuntimeHistoryBookmarkPage> ListBookmarksAsync(RuntimeHistoryBookmarkQuery query, CancellationToken cancellationToken = default);
    /// <exception cref="RuntimeHistoryUnavailableException">A normalized provider failure retains its original cause and localizing identifiers.</exception>
    ValueTask<RuntimeHistoryBookmark?> FindBookmarkAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default);
}

/// <summary>Records delivery observations at their own durable authority, without asserting execution effects.</summary>
public interface IRuntimeHistoryObservationWriter
{
    bool CanRecordHistory(IRuntimeHistoryStore selectedStore) => false;
    /// <exception cref="RuntimeHistoryUnavailableException">A normalized provider failure retains its original cause and localizing identifiers. This does not prove an interrupted commit rolled back.</exception>
    ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default);
}

/// <summary>Explicit owning retention mutation; inspection never calls this port. Default retention follows the run.</summary>
public interface IRuntimeHistoryRetentionStore
{
    /// <summary>Atomically purges event content through a committed position and returns the actual purged prefix.
    /// Retains policy, allocation and semantic replay receipts until run deletion. Missing history returns zero.</summary>
    /// <exception cref="RuntimeHistoryUnavailableException">A normalized provider failure retains its original cause and localizing identifiers. An interrupted commit may require reconciliation.</exception>
    ValueTask<long> PurgeThroughAsync(string workflowExecutionId, long throughSequence, CancellationToken cancellationToken = default);
}
