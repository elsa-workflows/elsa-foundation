using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>
/// Decision 11, the viewer's read path. A run is shown from its projected view plus the journal tail past the
/// projector's cursor, expanded in memory, so the view is always current, and only committed history is shown: the
/// viewer reads rows and never asks an owner for anything.
/// </summary>
public static class Viewer
{
    /// <summary>
    /// The tail read: the cursor first, then the view and the journal rows past the cursor. A round that commits between
    /// the reads is in both the view and the tail; <see cref="Merge"/> takes those rows once. Three logical store reads, the last a consistent per-instance query.
    /// </summary>
    public static async ValueTask<ViewRow?> ReadAsync(IKernelStore store, int partition, string instance, CancellationToken cancellationToken = default)
    {
        if (new InstanceRef(instance).Partition != partition)
            throw new ArgumentException("the partition must match the instance address", nameof(partition));
        var cursor = (await store.ProjectedAsync(partition, cancellationToken)).Position;
        var view = (await store.ReadViewsAsync([instance], cancellationToken)).SingleOrDefault();
        var tail = await store.ReadJournalOfAsync(partition, instance, cursor, cancellationToken);
        return Merge(instance, view, tail);
    }

    /// <summary>
    /// The view moved on by the tail, as a projector round would move it. A tail row the view already holds (a round that
    /// committed after the cursor was read) is skipped when its revision is already visible. A delete clears the
    /// previous run's view and count. Later revisions at the same key belong to the new run. The view keeps the
    /// committed creation's spelling: a lookup alias does not change Instance when catch-up moves rows into the view.
    /// </summary>
    public static ViewRow? Merge(string instance, ViewRow? view, IReadOnlyList<JournalRow> tail)
    {
        foreach (var row in tail.OrderBy(r => r.Position))
            if (row.Revision < 0) view = null;
            else if (row.Revision > (view?.Revision ?? 0)) view = new ViewRow(view?.Instance ?? row.Instance, row.Revision, (view?.Commits ?? 0) + 1);
        return view;
    }
}
