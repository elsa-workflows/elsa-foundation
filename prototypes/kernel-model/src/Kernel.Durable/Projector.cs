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
        finally { Done(); }
    }

    /// <summary>
    /// Decision 11, catch-up on request. Completes once the projection has reached the partition's position as the first
    /// round started after the request read it, so the projected rows then hold every commit made before the request; a
    /// full page, or a write another projector beat, is followed by another round. Requests are coalesced: all that
    /// arrive before such a round starts are served by it, so many viewers cause one. A round already under way when one
    /// arrives does not serve it (it may have read the cursor before). The task fails if a round fails, or if the
    /// projection cannot move on. The token cancels only this caller's wait; the rounds are not the caller's to cancel.
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
        if (start) _ = ServeAsync();
        return task.WaitAsync(cancellationToken);
    }

    // Rounds for the requests waiting now, until the projection reaches what the first of them read; those that come
    // meanwhile wait for the next serve (Done).
    private async Task ServeAsync()
    {
        TaskCompletionSource? served;
        lock (_gate) (served, _requested) = (_requested, null);
        try
        {
            long? target = null;
            for (var (stuck, best) = (0, -1L); ;)
            {
                var (at, written) = await OneAsync(CancellationToken.None);
                target ??= written;
                if (at >= target) break;
                // Progress is the projection moving on, by this projector or another (a lost race shows in the next
                // round's reading). Rounds that leave it where it was, three in a row, mean it cannot move on.
                (stuck, best) = at > best ? (0, at) : (stuck + 1, best);
                if (stuck > 2) throw new InvalidOperationException($"the projection of partition {partition} does not move past {best}");
            }
            served?.TrySetResult();
        }
        catch (Exception e) { served?.TrySetException(e); }
        finally { Done(); }
    }

    private void Done()
    {
        bool again;
        lock (_gate)
        {
            again = _requested is not null;
            if (!again) _running = false;
        }
        if (again) _ = ServeAsync();
    }

    // One round: where the projection is after it, as far as this round knows (after a lost write, where it read it:
    // the next round reads the new position), and the partition's position it read.
    private async Task<(long At, long Written)> OneAsync(CancellationToken cancellationToken)
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
        if (to <= from) return (from, written);
        return (await store.ProjectAsync(partition, from, to, next, cancellationToken) ? to : from, written);
    }
}
