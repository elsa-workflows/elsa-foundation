using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;

namespace Elsa.Testing;

/// <summary>
/// Spec 182's shared <see cref="IObservedSchemaFinalization"/> double: a test sets an observation directly, optionally
/// with the blockers an operator's status would carry as a pending version's counted members, and the fake counts what
/// asks it to refresh a stale observation or read a fresh status.
/// </summary>
internal class FakeObservedSchemaFinalization : IObservedSchemaFinalization
{
    private readonly Dictionary<string, (SchemaFamilyObservation Family, string[] Blockers)> _families = new(StringComparer.Ordinal);

    public int Refreshes { get; private set; }

    public TimeSpan? LastMaxAge { get; private set; }

    public Action? OnRefresh { get; set; }

    public bool FailStatus { get; set; }

    public int StatusReads { get; private set; }

    public void Set(SchemaFamilyObservation family, string[]? blockers = null) => _families[family.Family] = (family, blockers ?? []);

    public SchemaFamilyObservation? Find(string family) => _families.TryGetValue(family, out var found) ? found.Family : null;

    public IReadOnlyList<SchemaFamilyObservation> Observe() => _families.Values.Select(found => found.Family).ToArray();

    public ValueTask RefreshAsync(string family, TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        Refreshes++;
        LastMaxAge = maxAge;
        OnRefresh?.Invoke();
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default)
    {
        StatusReads++;
        if (FailStatus)
            throw new TimeoutException("The fleet could not be read.");
        return ValueTask.FromResult<IReadOnlyList<SchemaFamilyObservation>>(_families.Values
            .Select(found => found.Blockers.Length == 0
                ? found.Family
                : found.Family with { Pending = [new SchemaPendingVersion("2", false, [], found.Blockers)] })
            .ToArray());
    }
}
