using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>
/// Expands a partition's journal into the viewer's table, in position order. Its position is its own row, so it never
/// touches the fence row and may run on any node, several at once: the write moves the position only if it is still
/// where the read found it, so a round that lost the race, or whose process died between the read and the write, is
/// lost whole and done again by the next round.
/// </summary>
public sealed class Projector(IKernelStore store, int partition, int take = 100, Flaw flaws = Flaw.None)
{
    private static bool Has(Flaw flaws, Flaw flaw) => (flaws & flaw) != 0;

    private readonly object _gate = new();
    private bool _running;                      // a round is under way on this projector
    private TaskCompletionSource? _requested;   // catch-up requests that no round has started for yet

    /// <summary>Rounds this projector has run, whatever they found. For tests.</summary>
    public int Rounds { get; private set; }

    /// <summary>A round, as the host's timer or a nudge after commits runs it: nothing if one is under way, and no failure ever escapes.</summary>
    public async Task RoundAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_running) return;
            _running = true;
        }
        try { await OneAsync(cancellationToken); }
        catch (Exception) { /* a read or a write in doubt: the next round reads the position again */ }
        finally { Done(cancellationToken); }
    }

    /// <summary>
    /// Decision 11, catch-up on request. Completes once rounds that started after the request have brought the
    /// projection to the partition's position as they read it, so the projected rows then hold every commit made before
    /// the request: a full page, or a write that another projector beat, is followed by another round. Requests are
    /// coalesced: all that arrive before such a round starts are served by it, so many viewers cause one. A round
    /// already under way when one arrives does not serve it (it may have read the cursor before). The task fails if a
    /// round fails; the token is the first waiting request's.
    /// </summary>
    public Task CatchUpAsync(CancellationToken cancellationToken = default)
    {
        Task task;
        bool start;
        lock (_gate)
        {
            _requested ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            task = _requested.Task;
            start = !_running;
            if (start) _running = true;
        }
        if (start) _ = ServeAsync(cancellationToken);
        return task;
    }

    // Rounds for the requests waiting now, until the projection is current; those that come meanwhile wait for the next.
    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource? served;
        lock (_gate) (served, _requested) = (_requested, null);
        try
        {
            while (!await OneAsync(cancellationToken)) { }
            served?.TrySetResult();
        }
        catch (Exception e) { served?.TrySetException(e); }
        finally { Done(cancellationToken); }
    }

    private void Done(CancellationToken cancellationToken)
    {
        bool again;
        lock (_gate)
        {
            again = _requested is not null;
            if (!again) _running = false;
        }
        if (again) _ = ServeAsync(cancellationToken);
    }

    // One round. True when the projection is now at the partition's position as the round read it: it moved there, or
    // was there. False after a full page, or when another projector moved it first.
    private async Task<bool> OneAsync(CancellationToken cancellationToken)
    {
        Rounds++;
        var (from, written) = await store.ProjectedAsync(partition, cancellationToken);
        var rows = await store.ReadJournalAsync(partition, from, take, cancellationToken);
        // Positions are given out by the one writer under its fence, in the commit that writes the rows, so every
        // position up to the partition's is committed: one missing there was deleted (finding F15). Beyond it nothing
        // is read. With a full page the round stops at its last row; otherwise it has read everything up to written.
        var next = rows.Where(r => r.Position <= written).ToList();
        var to = rows.Count == take ? next.LastOrDefault()?.Position ?? from : written;
        if (Has(flaws, Flaw.ProjectorStopsAtGap))
        {
            next = rows.TakeWhile((row, i) => row.Position == from + i + 1).ToList();
            to = next.LastOrDefault()?.Position ?? from;
        }
        if (to <= from) return from >= written;
        return await store.ProjectAsync(partition, from, to, next, cancellationToken) && to >= written;
    }
}
