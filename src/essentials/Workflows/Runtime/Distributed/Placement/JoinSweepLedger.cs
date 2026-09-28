using System.Collections.Concurrent;

namespace Elsa.Workflows.Runtime.Distributed.Placement;

/// <summary>
/// The process's record of which shells have completed their join sweep (spec 184, FR-022), so the sweep runs only the
/// first time a shell's distributed runtime activates in the process, and never on a shell reload or a rejoin after a
/// lapse: the leases under the host id are then the process's own.
/// </summary>
/// <remarks>
/// It is process-wide by default (<see cref="Process"/>), because a shell container and its singletons are rebuilt on a
/// reload while the process, and so the leases it wrote, stay. A test that simulates a restart supplies its own ledger
/// for the new "process". The ledger also holds the instant the process first began a join sweep for a host id: every
/// lease this process writes under that host id is later, so a sweep releases only what was acquired at or before it,
/// even in a store another shell of the same process already uses.
/// </remarks>
public sealed class JoinSweepLedger
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _firstSweepStartedAt = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string HostId, string Shell), bool> _completed = new();

    /// <summary>The ledger of this process.</summary>
    public static JoinSweepLedger Process { get; } = new();

    /// <summary>The instant this process first began a join sweep under <paramref name="hostId"/>, recording
    /// <paramref name="now"/> when none has begun yet. Only leases acquired at or before it are the predecessor's.</summary>
    public DateTimeOffset Begin(string hostId, DateTimeOffset now) => _firstSweepStartedAt.GetOrAdd(hostId, now);

    public bool IsComplete(string hostId, string shell) => _completed.ContainsKey((hostId, shell));

    public void Complete(string hostId, string shell) => _completed.TryAdd((hostId, shell), true);
}
