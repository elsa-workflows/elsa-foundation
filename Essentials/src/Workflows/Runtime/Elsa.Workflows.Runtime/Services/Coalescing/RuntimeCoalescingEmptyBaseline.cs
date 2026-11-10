namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>
/// What a coalescing session knows about the rows one durable store holds for its run, so that a point read of a row
/// the store cannot hold is answered without a query.
/// </summary>
/// <remarks>
/// The knowledge is an observation, never an inference from the run having no record: one probe of the store, made
/// under the run's single-writer lease, found no row of the run. From then on the store can hold only the rows this
/// session sent to it, and each of those is recorded before its write is attempted, so a write whose outcome is
/// uncertain still counts as possibly durable. The probe saw one persistence scope, so the baseline answers only for
/// a read under that scope. A baseline answers nothing until <see cref="Begin"/>, and nothing again after
/// <see cref="End"/>. The session is confined to one drain, but <see cref="End"/> can arrive from another thread: a
/// cancellation callback, or a flow that captured the session. The lock keeps a read consistent with that.
/// </remarks>
internal sealed class RuntimeCoalescingEmptyBaseline
{
    private readonly HashSet<string> _possiblyDurable = new(StringComparer.Ordinal);
    private bool _begun;
    private bool _ended = true;
    private string? _emptyScope;

    /// <summary>
    /// Lets the baseline answer. Only the first call counts, and not after <see cref="End"/>: a baseline that was
    /// ended never answers again.
    /// </summary>
    public void Begin()
    {
        lock (_possiblyDurable)
        {
            if (_begun)
                return;

            _begun = true;
            _ended = false;
        }
    }

    /// <summary>Whether this baseline has stopped answering for good.</summary>
    public bool HasEnded
    {
        get
        {
            lock (_possiblyDurable)
                return _ended;
        }
    }

    /// <summary>Whether the store still has to be probed before <see cref="IsKnownAbsent"/> can answer.</summary>
    public bool NeedsProbe
    {
        get
        {
            lock (_possiblyDurable)
                return !_ended && _emptyScope is null;
        }
    }

    /// <summary>
    /// Records the one probe: whether the store held no row of the run under <paramref name="scope"/>. A store
    /// that held a row ends the baseline, because every row is then read from the store.
    /// </summary>
    public void RecordProbe(bool storeWasEmpty, string scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!storeWasEmpty)
        {
            End();
            return;
        }

        lock (_possiblyDurable)
            _emptyScope ??= scope;
    }

    /// <summary>
    /// Whether the store cannot hold this row under <paramref name="scope"/>, so a read of it returns nothing
    /// without a query.
    /// </summary>
    public bool IsKnownAbsent(string rowId, string scope)
    {
        lock (_possiblyDurable)
            return !_ended && _emptyScope is not null && StringComparer.Ordinal.Equals(_emptyScope, scope) && !_possiblyDurable.Contains(rowId);
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
            _begun = true;
            _ended = true;
            _possiblyDurable.Clear();
        }
    }
}
