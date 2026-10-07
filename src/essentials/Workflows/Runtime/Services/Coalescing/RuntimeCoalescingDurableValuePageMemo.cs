using System.Runtime.CompilerServices;
using System.Text;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>
/// A bounded, session-local memo for detached raw durable-value pages. The owning coalescing session remains
/// responsible for eligibility and for applying its current staged overlay to every returned page.
/// </summary>
public sealed class RuntimeCoalescingDurableValuePageMemo
{
    private const int MaximumPages = 32;
    private const int MaximumRows = 1_024;
    private const long MaximumContentBytes = 4 * 1024 * 1024;
    private const long PageOverheadBytes = 128;
    private const long KeyOverheadBytes = 96;
    private const long RowOverheadBytes = 256;
    private const long MetadataEntryOverheadBytes = 32;

    private readonly object _gate = new();
    private readonly Dictionary<MemoKey, Entry> _entries = new();
    private long _generation;
    private long _activeWrites;
    private int _rowCount;
    private long _contentBytes;
    private bool _capacityDisabled;
    private bool _permanentlyDisabled;

    /// <summary>
    /// Returns a fresh detached page from the memo, or invokes <paramref name="loadPageAsync"/> and returns its
    /// complete result. Provider calls run outside the memo lock and retain their original errors and cancellations.
    /// </summary>
    public async ValueTask<RuntimeStorePage<DurableValueState>> GetOrLoadAsync(
        DurableValueStatePageQuery query,
        PersistenceAccessContext accessContext,
        object continuationCodecIdentity,
        Func<ValueTask<RuntimeStorePage<DurableValueState>>> loadPageAsync)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(accessContext);
        ArgumentNullException.ThrowIfNull(continuationCodecIdentity);
        ArgumentNullException.ThrowIfNull(loadPageAsync);

        var key = new MemoKey(query, accessContext, continuationCodecIdentity);
        Entry? hit = null;
        long readGeneration = 0;
        long hitGeneration = 0;
        var mayAdmit = false;

        lock (_gate)
        {
            if (CanReadOrAdmit())
            {
                if (_entries.TryGetValue(key, out hit))
                {
                    // The immutable internal snapshot is cloned after releasing the cache lock.
                    hitGeneration = _generation;
                }
                else
                {
                    readGeneration = _generation;
                    mayAdmit = true;
                }
            }
        }

        if (hit is not null)
        {
            try
            {
                var detachedHit = ClonePage(hit.Page, query);
                if (IsCurrentHit(key, hit, hitGeneration))
                    return detachedHit;
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                RemoveIfCurrent(key, hit);
                mayAdmit = IsCurrentGeneration(hitGeneration);
                readGeneration = hitGeneration;
            }

            // A write, capacity boundary, or owner disable raced with the clone. This call began before that
            // boundary, so it must use the provider and must not refill the newer generation.
            if (!IsCurrentGeneration(hitGeneration))
                mayAdmit = false;

            RemoveIfCurrent(key, hit);
        }

        var loadedPage = await loadPageAsync();
        if (!mayAdmit)
            return loadedPage;

        PageEstimate estimate;
        try
        {
            estimate = EstimateContent(key, query, loadedPage);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            if (exception is PageContentLimitException)
                DisableForCapacity(readGeneration, key);

            return loadedPage;
        }

        Entry admitted;
        lock (_gate)
        {
            if (!CanReadOrAdmit() || _generation != readGeneration)
                return loadedPage;

            // A concurrent miss for this exact key already paid the entry cost. Check it before testing capacity so
            // its duplicate provider completion cannot evict the cache or disable a healthy generation.
            if (_entries.ContainsKey(key))
                return loadedPage;

            if (_entries.Count >= MaximumPages || WouldExceedContentBudget(estimate))
            {
                DisableForCapacityUnderLock();
                return loadedPage;
            }

            RuntimeStorePage<DurableValueState> snapshot;
            try
            {
                // The provider result was accounted before cloning. Recheck the detached copy as provider-owned
                // lists and metadata maps can be mutable between those two observations.
                snapshot = ClonePage(loadedPage, query);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                return loadedPage;
            }

            PageEstimate snapshotEstimate;
            try
            {
                snapshotEstimate = EstimateContent(key, query, snapshot);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                if (exception is PageContentLimitException)
                    DisableForCapacityUnderLock();

                return loadedPage;
            }

            if (WouldExceedContentBudget(snapshotEstimate))
            {
                DisableForCapacityUnderLock();
                return loadedPage;
            }

            admitted = new Entry(snapshot, snapshotEstimate.RowCount, snapshotEstimate.ContentBytes);
            _entries.Add(key, admitted);
            _rowCount = checked(_rowCount + snapshotEstimate.RowCount);
            _contentBytes = checked(_contentBytes + snapshotEstimate.ContentBytes);
        }

        try
        {
            // Do not expose the private cache-owned snapshot to the existing overlay merger.
            return ClonePage(admitted.Page, query);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            RemoveIfCurrent(key, admitted);
            return loadedPage;
        }
    }

    /// <summary>Begins an actual provider-write boundary and returns a lease that must be completed successfully.</summary>
    public WriteLease BeginWrite()
    {
        lock (_gate)
        {
            AdvanceGenerationUnderLock();
            ClearEntriesUnderLock();
            if (_activeWrites < long.MaxValue)
                _activeWrites++;
            else
                _permanentlyDisabled = true;
        }

        return new WriteLease(this);
    }

    /// <summary>Permanently ends admission for this memo when its owner is nested, disposed, or loses authority.</summary>
    public void DisablePermanently()
    {
        lock (_gate)
        {
            AdvanceGenerationUnderLock();
            ClearEntriesUnderLock();
            _permanentlyDisabled = true;
        }
    }

    private bool CanReadOrAdmit() =>
        !_capacityDisabled && !_permanentlyDisabled && _activeWrites == 0;

    // Both byte operands are guarded by the 4 MiB cap, so this checked sum is at most 8 MiB.
    private bool WouldExceedContentBudget(PageEstimate estimate) =>
        estimate.RowCount > MaximumRows - _rowCount ||
        checked(_contentBytes + estimate.ContentBytes) > MaximumContentBytes;

    private void CompleteWrite(bool succeeded)
    {
        lock (_gate)
        {
            AdvanceGenerationUnderLock();
            ClearEntriesUnderLock();
            if (_activeWrites > 0)
                _activeWrites--;

            if (!succeeded)
                _permanentlyDisabled = true;
            else if (_activeWrites == 0 && !_permanentlyDisabled)
                _capacityDisabled = false;
        }
    }

    private void AdvanceGenerationUnderLock()
    {
        if (_generation == long.MaxValue)
        {
            _permanentlyDisabled = true;
            return;
        }

        _generation++;
    }

    private void DisableForCapacity(long readGeneration, MemoKey key)
    {
        lock (_gate)
        {
            if (_generation == readGeneration && _activeWrites == 0 && !_permanentlyDisabled && !_entries.ContainsKey(key))
                DisableForCapacityUnderLock();
        }
    }

    private void DisableForCapacityUnderLock()
    {
        ClearEntriesUnderLock();
        _capacityDisabled = true;
    }

    private void ClearEntriesUnderLock()
    {
        _entries.Clear();
        _rowCount = 0;
        _contentBytes = 0;
    }

    private void RemoveIfCurrent(MemoKey key, Entry entry)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var current) || !ReferenceEquals(current, entry))
                return;

            _entries.Remove(key);
            _rowCount -= entry.RowCount;
            _contentBytes -= entry.ContentBytes;
        }
    }

    private bool IsCurrentHit(MemoKey key, Entry entry, long generation)
    {
        lock (_gate)
            return CanReadOrAdmit() && _generation == generation &&
                   _entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry);
    }

    private bool IsCurrentGeneration(long generation)
    {
        lock (_gate)
            return CanReadOrAdmit() && _generation == generation;
    }

    private static PageEstimate EstimateContent(
        MemoKey key,
        DurableValueStatePageQuery query,
        RuntimeStorePage<DurableValueState> page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var rows = page.Items;
        var rowCount = rows.Count;
        if (rowCount > query.Limit)
            throw new PageContentLimitException();

        var total = PageOverheadBytes;
        AddString(ref total, key.ScopeValue);
        AddString(ref total, key.PurposeValue);
        AddString(ref total, key.WorkflowExecutionId);
        AddString(ref total, key.InputContinuationToken);
        AddString(ref total, page.NextContinuationToken);
        total = AddContentBytes(total, KeyOverheadBytes);

        for (var index = 0; index < rowCount; index++)
        {
            var row = rows[index] ?? throw new InvalidOperationException("A durable-value page cannot contain a null row.");
            total = AddContentBytes(total, RowOverheadBytes);
            AddString(ref total, row.DurableValueId);
            AddString(ref total, row.WorkflowExecutionId);
            AddString(ref total, row.ValueId);
            AddString(ref total, row.Type.Kind);
            AddString(ref total, row.Type.Id);
            AddJson(ref total, row.Type.Schema);
            AddJson(ref total, row.InlineValue);
            AddString(ref total, row.SourceActivityExecutionId);

            if (row.ExternalReference is { } externalReference)
            {
                AddString(ref total, externalReference.StorageProfile);
                AddString(ref total, externalReference.Locator);
                AddMetadata(ref total, externalReference.Metadata);
            }

            AddMetadata(ref total, row.Metadata);
        }

        if (total > MaximumContentBytes)
            throw new PageContentLimitException();

        return new PageEstimate(rowCount, total);
    }

    private static void AddMetadata(ref long total, IReadOnlyDictionary<string, string> metadata)
    {
        foreach (var item in metadata)
        {
            total = AddContentBytes(total, MetadataEntryOverheadBytes);
            AddString(ref total, item.Key);
            AddString(ref total, item.Value);
        }
    }

    private static void AddJson(ref long total, System.Text.Json.JsonElement? element)
    {
        if (!element.HasValue)
            return;

        var rawJson = element.Value.GetRawText();
        total = AddContentBytes(total, Encoding.UTF8.GetByteCount(rawJson));
    }

    private static void AddString(ref long total, string? value)
    {
        if (value is not null)
            total = AddContentBytes(total, Encoding.UTF8.GetByteCount(value));
    }

    private static long AddContentBytes(long total, long additionalBytes)
    {
        // Each accepted partial estimate is at most 4 MiB and each string byte count is bounded by Int32,
        // so Int64 overflow is unreachable before the earlier content cap. Checked addition protects future edits.
        total = checked(total + additionalBytes);
        if (total > MaximumContentBytes)
            throw new PageContentLimitException();

        return total;
    }

    private static RuntimeStorePage<DurableValueState> ClonePage(
        RuntimeStorePage<DurableValueState> page,
        DurableValueStatePageQuery query)
    {
        var sourceRows = page.Items;
        var rows = new DurableValueState[sourceRows.Count];
        for (var index = 0; index < sourceRows.Count; index++)
            rows[index] = CloneRow(sourceRows[index]);

        return new RuntimeStorePage<DurableValueState>(query, rows, page.NextContinuationToken);
    }

    private static DurableValueState CloneRow(DurableValueState row)
    {
        var type = new RuntimeValueTypeDescriptor(row.Type.Kind, row.Type.Id, row.Type.Schema?.Clone());
        var externalReference = row.ExternalReference is { } reference
            ? new DurableValueExternalReference(reference.StorageProfile, reference.Locator, reference.Metadata)
            : null;

        return new DurableValueState(
            row.DurableValueId,
            row.WorkflowExecutionId,
            row.ValueId,
            type,
            row.Lifecycle,
            row.Storage,
            row.InlineValue?.Clone(),
            externalReference,
            row.SourceActivityExecutionId,
            row.CapturedAt,
            RuntimeModelMetadata.Snapshot(row.Metadata));
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    private sealed class PageContentLimitException : Exception { }

    private readonly record struct PageEstimate(int RowCount, long ContentBytes);

    private sealed record Entry(
        RuntimeStorePage<DurableValueState> Page,
        int RowCount,
        long ContentBytes);

    private readonly struct MemoKey : IEquatable<MemoKey>
    {
        public MemoKey(
            DurableValueStatePageQuery query,
            PersistenceAccessContext accessContext,
            object continuationCodecIdentity)
        {
            ScopeValue = accessContext.Scope?.Value;
            AccessPolicy = accessContext.AccessPolicy;
            PurposeValue = accessContext.Purpose?.Value;
            AcrossScopes = accessContext.AcrossScopes;
            WorkflowExecutionId = query.WorkflowExecutionId;
            Limit = query.Limit;
            InputContinuationToken = query.ContinuationToken;
            ContinuationCodecIdentity = continuationCodecIdentity;
        }

        public string? ScopeValue { get; }
        public PersistenceAccessPolicy AccessPolicy { get; }
        public string? PurposeValue { get; }
        public bool AcrossScopes { get; }
        public string WorkflowExecutionId { get; }
        public int Limit { get; }
        public string? InputContinuationToken { get; }
        public object ContinuationCodecIdentity { get; }

        public bool Equals(MemoKey other) =>
            StringComparer.Ordinal.Equals(ScopeValue, other.ScopeValue) &&
            AccessPolicy == other.AccessPolicy &&
            StringComparer.Ordinal.Equals(PurposeValue, other.PurposeValue) &&
            AcrossScopes == other.AcrossScopes &&
            StringComparer.Ordinal.Equals(WorkflowExecutionId, other.WorkflowExecutionId) &&
            Limit == other.Limit &&
            StringComparer.Ordinal.Equals(InputContinuationToken, other.InputContinuationToken) &&
            ReferenceEquals(ContinuationCodecIdentity, other.ContinuationCodecIdentity);

        public override bool Equals(object? obj) => obj is MemoKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(ScopeValue, StringComparer.Ordinal);
            hash.Add(AccessPolicy);
            hash.Add(PurposeValue, StringComparer.Ordinal);
            hash.Add(AcrossScopes);
            hash.Add(WorkflowExecutionId, StringComparer.Ordinal);
            hash.Add(Limit);
            hash.Add(InputContinuationToken, StringComparer.Ordinal);
            hash.Add(RuntimeHelpers.GetHashCode(ContinuationCodecIdentity));
            return hash.ToHashCode();
        }
    }

    /// <summary>A single actual write boundary whose uncompleted disposal permanently disables this memo.</summary>
    public sealed class WriteLease : IDisposable
    {
        private RuntimeCoalescingDurableValuePageMemo? _memo;

        internal WriteLease(RuntimeCoalescingDurableValuePageMemo memo) => _memo = memo;

        /// <summary>Marks the actual provider write as successful, allowing capacity to reset if this was the last write.</summary>
        public void Succeed() => Complete(succeeded: true);

        public void Dispose() => Complete(succeeded: false);

        private void Complete(bool succeeded)
        {
            var memo = Interlocked.Exchange(ref _memo, null);
            memo?.CompleteWrite(succeeded);
        }
    }
}
