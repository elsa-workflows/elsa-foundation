namespace Kernel.Durable;

public sealed partial class InMemoryStore
{
    internal sealed record RunHistoryRow(string Reference, string Type, string? Definition, InstanceStatus Status, long UpdatedAt);
    internal sealed record RunHistoryBookmark(long UpdatedAt, string Reference);
    internal sealed record RunHistoryPage(IReadOnlyList<RunHistoryRow> Rows, long AsOf);

    private readonly Dictionary<string, Dictionary<string, RunHistoryRow>> _foundationRunHistory = new(StringComparer.Ordinal);

    private void ProjectRunHistory(ReferenceBinding binding, InstanceRow row, long now)
    {
        if (!_foundationRunHistory.TryGetValue(binding.Tenant, out var tenant))
            _foundationRunHistory[binding.Tenant] = tenant = new(StringComparer.Ordinal);
        var definition = _foundationHeaders.GetValueOrDefault(binding.Address)?.Labels.GetValueOrDefault("definition");
        tenant[binding.Reference] = new(binding.Reference, row.Model, definition, row.Status, now);
    }

    private void RemoveRunHistory(CreationAddress address)
    {
        if (!_referenceBirths.TryGetValue(address, out var binding) ||
            !_foundationRunHistory.TryGetValue(binding.Tenant, out var tenant)) return;
        tenant.Remove(binding.Reference);
        if (tenant.Count == 0) _foundationRunHistory.Remove(binding.Tenant);
    }

    internal RunHistoryPage ReadFoundationRunHistory(string tenant, string type, string? definition, InstanceStatus? status,
        decimal? from, decimal? to, RunHistoryBookmark? after, int take)
    {
        lock (_gate)
        {
            var asOf = clock();
            if (!_foundationRunHistory.TryGetValue(tenant, out var rows)) return new([], asOf);
            var ordered = rows.Values.Where(row => row.Type == type &&
                (definition is null || row.Definition == definition) &&
                (status is null || row.Status == status) &&
                (from is null || row.UpdatedAt >= from) && (to is null || row.UpdatedAt <= to) &&
                (after is null || row.UpdatedAt < after.UpdatedAt ||
                    row.UpdatedAt == after.UpdatedAt && StringComparer.Ordinal.Compare(row.Reference, after.Reference) > 0));
            return new(ordered.OrderByDescending(row => row.UpdatedAt)
                .ThenBy(row => row.Reference, StringComparer.Ordinal).Take(take).ToArray(), asOf);
        }
    }
}
