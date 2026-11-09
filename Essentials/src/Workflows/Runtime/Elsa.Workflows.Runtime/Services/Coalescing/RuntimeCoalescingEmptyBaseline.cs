namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>
/// What a coalescing session knows about the rows one durable store holds for its run, so that a point read of a row
/// the store cannot hold is answered without a query.
/// </summary>
/// <remarks>
/// The knowledge is an observation, never an inference from the run having no record: one probe of the store, made
/// under the run's single-writer lease, found no row of the run. From then on the store can hold only the rows this
/// session sent to it, and each of those is recorded before its write is attempted, so a write whose outcome is
/// uncertain still counts as possibly durable. A run that had a durable record when its drain began is never probed,
/// because its rows are there to be read. The session is confined to one drain and is not thread-safe. The lock here
/// is uncontended in a drain; it only keeps a read consistent should it ever overlap a recorded write.
/// </remarks>
internal sealed class RuntimeCoalescingEmptyBaseline(bool runHadNoRecord)
{
    private readonly HashSet<string> _possiblyDurable = new(StringComparer.Ordinal);
    private bool _ended = !runHadNoRecord;
    private bool? _storeWasEmpty;

    /// <summary>Whether the store still has to be probed before <see cref="IsKnownAbsent"/> can answer.</summary>
    public bool NeedsProbe
    {
        get
        {
            lock (_possiblyDurable)
                return !_ended && _storeWasEmpty is null;
        }
    }

    /// <summary>Records the one probe: whether the store held no row of the run.</summary>
    public void RecordProbe(bool storeWasEmpty)
    {
        lock (_possiblyDurable)
            _storeWasEmpty ??= storeWasEmpty;
    }

    /// <summary>Whether the store cannot hold this row, so a read of it returns nothing without a query.</summary>
    public bool IsKnownAbsent(string rowId)
    {
        lock (_possiblyDurable)
            return !_ended && _storeWasEmpty == true && !_possiblyDurable.Contains(rowId);
    }

    /// <summary>Records a row about to be written to the store, whether or not the write then lands.</summary>
    public void RecordPossibleWrite(string rowId)
    {
        lock (_possiblyDurable)
        {
            if (!_ended)
                _possiblyDurable.Add(rowId);
        }
    }

    /// <summary>
    /// Stops answering for good. Used when rows of the run may reach the store by a path this session does not see.
    /// </summary>
    public void End()
    {
        lock (_possiblyDurable)
        {
            _ended = true;
            _possiblyDurable.Clear();
        }
    }
}
