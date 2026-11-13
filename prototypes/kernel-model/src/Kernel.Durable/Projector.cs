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

    private bool _running;

    public async Task RoundAsync(CancellationToken cancellationToken = default)
    {
        if (_running) return;
        _running = true;
        try
        {
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
            if (to > from) await store.ProjectAsync(partition, from, to, next, cancellationToken);
        }
        catch (Exception) { /* a read or a write in doubt: the next round reads the position again */ }
        finally { _running = false; }
    }
}
