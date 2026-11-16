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
    private readonly record struct Page(long From, long At, long Written, long ContiguousThrough);

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
    /// round started after the request read it. If part of that prefix disappeared, it also projects the committed
    /// deletion markers that account for it. Both targets are bounded. Requests are coalesced: all that
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
        // A store may complete every page synchronously and cancel this token inside its first read. Register this
        // caller's wait before starting those pages, so a later successful shared result cannot hide cancellation.
        var waiting = task.WaitAsync(cancellationToken);
        // Shared provider work may block synchronously. Dispatch it independently so the initiating caller
        // receives its cancellable task before any page can hold that caller's stack.
        if (start) _ = Task.Run(ServeAsync);
        return waiting;
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
            long? deleteBound = null;
            bool needsDeleteBound = false;
            long best = -1;
            for (int stuck = 0; ;)
            {
                var page = await OneAsync(CancellationToken.None, observeGaps: true);
                target ??= page.Written;
                // Another projector can cross a gap between our pages, including after a lost conditional write.
                if ((best >= 0 && best < target && page.From > best) ||
                    page.ContiguousThrough < Math.Min(page.At, target.Value))
                    needsDeleteBound = true;
                // A fresh bound read after crossing the requested prefix includes the permanent markers for
                // every deletion that hid one of its revisions. Later appends cannot extend this second bound.
                if (needsDeleteBound && (best >= target || page.From >= target)) deleteBound ??= page.Written;
                if (page.At >= target && (!needsDeleteBound || deleteBound is { } bound && page.At >= bound)) break;
                // Progress is the projection moving on, by this projector or another (a lost race shows in the next
                // round's reading). Rounds that leave it where it was, three in a row, mean it cannot move on.
                (stuck, best) = page.At > best ? (0, page.At) : (stuck + 1, best);
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
        if (again) _ = Task.Run(ServeAsync);
    }

    // One round: where the projection is after it, as far as this round knows (after a lost write, where it read it:
    // the next round reads the new position), and the partition's position it read.
    private async Task<Page> OneAsync(CancellationToken cancellationToken, bool observeGaps = false)
    {
        Rounds++;
        var (from, written) = await store.ProjectedAsync(partition, cancellationToken);
        var rows = await store.ReadJournalAsync(partition, from, take, cancellationToken);
        // Every returned position is committed under the partition fence. A delete between the bound and page reads
        // can remove older rows and append its marker beyond that bound; include it instead of acknowledging a gap
        // while leaving an obsolete view. A full page stops at its last row. A short page also crosses deleted gaps
        // up to the earlier bound, without moving backwards when the page contains newer committed rows.
        var next = rows.ToList();
        var last = next.LastOrDefault()?.Position ?? from;
        var to = rows.Count == take ? last : Math.Max(written, last);
        if (Has(flaws, Flaw.ProjectorStopsAtGap))
        {
            next = rows.TakeWhile((row, i) => row.Position == from + i + 1).ToList();
            to = next.LastOrDefault()?.Position ?? from;
        }
        if (to <= from || !await store.ProjectAsync(partition, from, to, next, cancellationToken))
            return new(from, from, written, from);
        var contiguous = to;
        if (observeGaps && to - from != next.Count)
        {
            contiguous = from;
            foreach (var row in next)
            {
                if (row.Position - contiguous != 1) break;
                contiguous = row.Position;
            }
        }
        return new(from, to, written, contiguous);
    }
}
