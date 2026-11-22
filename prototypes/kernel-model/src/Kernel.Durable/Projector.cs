using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>
/// Expands a partition's journal into the viewer's table, in position order. Its position is its own row, so it never
/// touches the fence row and may run on any node, several at once: the write moves the position only if it is still
/// where the read found it, so a round that lost the race, or whose process died between the read and the write, is
/// lost whole. A later round reads the position again: <see cref="NodeProjection"/> runs one after a delay when a round
/// threw or lost its write, and a take of the partition nudges one where the projecting process died.
/// </summary>
public sealed class Projector(IKernelStore store, int partition, int take = 100, Flaw flaws = Flaw.None)
{
    private readonly record struct Page(long From, long At, long Written, long ContiguousThrough, bool Lost = false);

    private static bool Has(Flaw flaws, Flaw flaw) => (flaws & flaw) != 0;

    private readonly object _gate = new();
    private bool _running;                      // a round is under way on this projector
    private TaskCompletionSource? _requested;   // catch-up requests that no round has started for yet

    /// <summary>Rounds this projector has run, whatever they found. For tests.</summary>
    public int Rounds { get; private set; }

    /// <summary>
    /// A round, as a nudge after commits runs it (<see cref="NodeProjection"/>): nothing if one is under way, and no
    /// failure ever escapes. <see cref="RoundResult.More"/> when it moved the projection and left it behind the position it
    /// read; <see cref="RoundResult.Failed"/> when it threw or lost its conditional write, and a later round must read the
    /// position again.
    /// </summary>
    public async Task<RoundResult> RoundAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_running) return RoundResult.Done;      // the round under way reads the position itself
            _running = true;
        }
        try
        {
            var page = await OneAsync(cancellationToken);
            return page.Lost ? RoundResult.Failed : page.At > page.From && page.At < page.Written ? RoundResult.More : RoundResult.Done;
        }
        catch (Exception) { return RoundResult.Failed; }      // a read or a write in doubt
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
        if (to <= from) return new(from, from, written, from);
        if (!await store.ProjectAsync(partition, from, to, next, cancellationToken)) return new(from, from, written, from, Lost: true);
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

/// <summary>What a projector round found (<see cref="Projector.RoundAsync"/>).</summary>
public enum RoundResult { Done, More, Failed }

/// <summary>
/// The projection of one node's set (K1): a projector per virtual partition, and rounds only for partitions whose journal
/// moved. A landed commit nudges the partitions whose position it moved, a commit whose outcome is unknown the ones it
/// may have moved, and a take the partition it took. One drain at a time runs a round for each nudged partition, again
/// at once while a round moves on and leaves more, and again after a delay that doubles up to a second while a round
/// throws or loses its write. Nothing sweeps every partition on a timer. A partition that moved to another node is
/// projected there; the cursor write is conditional, so rounds of two nodes race safely.
/// </summary>
public sealed class NodeProjection(IKernelStore store, int take = 100, Flaw flaws = Flaw.None)
{
    private readonly Dictionary<int, Projector> _projectors = [];
    private readonly SortedSet<int> _dirty = [];
    private Task _drain = Task.CompletedTask;
    private bool _draining;
    private readonly Dictionary<int, int> _failures = [];      // rounds in a row that threw or lost their write, per partition

    /// <summary>Rounds run so far, over every partition. For tests.</summary>
    public int Rounds { get { lock (_dirty) return _projectors.Values.Sum(p => p.Rounds); } }

    /// <summary>Rounds run so far for one partition. For tests.</summary>
    public int RoundsOf(int partition) { lock (_dirty) return _projectors.TryGetValue(partition, out var projector) ? projector.Rounds : 0; }

    /// <summary>Done when no nudged partition waits for a round. For tests.</summary>
    public Task Idle { get { lock (_dirty) return _drain; } }

    public void Nudge(IEnumerable<int> partitions)
    {
        lock (_dirty)
        {
            _dirty.UnionWith(partitions);
            if (_draining || _dirty.Count == 0) return;
            _draining = true;
            _drain = Task.Run(DrainAsync);
        }
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            int[] nudged;
            lock (_dirty)
            {
                if (_dirty.Count == 0) { _draining = false; return; }
                nudged = [.. _dirty];
                _dirty.Clear();
            }
            foreach (var partition in nudged)
            {
                Projector projector;
                lock (_dirty) projector = _projectors.TryGetValue(partition, out var known) ? known : _projectors[partition] = new(store, partition, take, flaws);
                var result = await projector.RoundAsync();
                var failures = 0;
                lock (_dirty)
                {
                    if (result == RoundResult.More) _dirty.Add(partition);     // a full page: another round at once
                    if (result == RoundResult.Failed) failures = _failures[partition] = _failures.GetValueOrDefault(partition) + 1;
                    else _failures.Remove(partition);
                }
                if (failures > 0)
                    _ = Task.Delay(TimeSpan.FromMilliseconds(Math.Min(1000, 10 << Math.Min(failures, 7)))).ContinueWith(_ => Nudge([partition]), TaskScheduler.Default);
            }
        }
    }
}
