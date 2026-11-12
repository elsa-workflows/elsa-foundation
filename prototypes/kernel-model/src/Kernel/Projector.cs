namespace Kernel;

/// <summary>
/// Expands a partition's journal into the viewer's table, in position order. Its position is its own row, so it never
/// touches the fence row and may run on any node, several at once: the write moves the position only if it is still
/// where the read found it, so a round that lost the race, or whose process died between the read and the write, is
/// lost whole and done again by the next round.
/// </summary>
public sealed class Projector(IKernelStore store, int partition, int take = 100)
{
    private bool _running;

    public async Task RoundAsync(CancellationToken cancellationToken = default)
    {
        if (_running) return;
        _running = true;
        try
        {
            var from = await store.ProjectedAsync(partition, cancellationToken);
            var rows = await store.ReadJournalAsync(partition, from, take, cancellationToken);
            // Positions are given out by the one writer under its fence, so they are dense: stop at the first that is not written.
            var next = rows.TakeWhile((row, i) => row.Position == from + i + 1).ToList();
            if (next.Count > 0) await store.ProjectAsync(partition, from, next, cancellationToken);
        }
        catch (Exception) { /* a read or a write in doubt: the next round reads the position again */ }
        finally { _running = false; }
    }
}
