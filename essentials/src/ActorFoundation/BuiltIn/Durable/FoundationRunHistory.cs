namespace Kernel.Durable;

public sealed partial class InMemoryStore
{
    internal sealed record RunHistoryRow(string Reference, string? Definition, InstanceStatus Status, long UpdatedAt);
    internal sealed record RunHistoryBookmark(long UpdatedAt, string Reference);
    internal sealed record RunHistoryPage(IReadOnlyList<RunHistoryRow> Rows, long AsOf);

    private readonly Dictionary<(string Tenant, string ActorType), Dictionary<string, RunHistoryRow>> _foundationRunHistory = [];

    private void ProjectRunHistory(ReferenceBinding binding, InstanceRow row, long now)
    {
        var bucket = (binding.Tenant, row.Model);
        if (!_foundationRunHistory.TryGetValue(bucket, out var rows))
            _foundationRunHistory[bucket] = rows = new(StringComparer.Ordinal);
        var definition = _foundationHeaders.GetValueOrDefault(binding.Address)?.Labels.GetValueOrDefault("definition");
        rows[binding.Reference] = new(binding.Reference, definition, row.Status, now);
    }

    private void RemoveRunHistory(CreationAddress address, string actorType)
    {
        if (!_referenceBirths.TryGetValue(address, out var binding)) return;
        var bucket = (binding.Tenant, actorType);
        if (!_foundationRunHistory.TryGetValue(bucket, out var rows)) return;
        rows.Remove(binding.Reference);
        if (rows.Count == 0) _foundationRunHistory.Remove(bucket);
    }

    internal RunHistoryPage ReadFoundationRunHistory(string tenant, string type, string? definition, InstanceStatus? status,
        decimal? from, decimal? to, RunHistoryBookmark? after, int take)
    {
        RunHistoryRow[] snapshot;
        long asOf;
        lock (_gate)
        {
            asOf = clock();
            if (!_foundationRunHistory.TryGetValue((tenant, type), out var rows)) return new([], asOf);
            snapshot = rows.Values.ToArray();
        }

        var matching = snapshot.Where(row => (definition is null || row.Definition == definition) &&
            (status is null || row.Status == status) &&
            (from is null || row.UpdatedAt >= from) && (to is null || row.UpdatedAt <= to) &&
            (after is null || row.UpdatedAt < after.UpdatedAt ||
                row.UpdatedAt == after.UpdatedAt && StringComparer.Ordinal.Compare(row.Reference, after.Reference) > 0));
        return new(matching.OrderByDescending(row => row.UpdatedAt)
            .ThenBy(row => row.Reference, StringComparer.Ordinal).Take(take).ToArray(), asOf);
    }
}
