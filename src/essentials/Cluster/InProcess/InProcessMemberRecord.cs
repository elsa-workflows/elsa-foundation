using System.Collections.Concurrent;
using Elsa.Cluster.Core.Models;

namespace Elsa.Cluster.InProcess;

/// <summary>
/// The in-process member of this process under one host id: its incarnation and its latest published report.
/// </summary>
/// <remarks>
/// A shell container is built from copies of the host's registrations, so each shell constructs its own
/// <see cref="InProcessClusterMembership"/>. Keeping the member here, once per process, is what makes every shell see
/// the same incarnation and the same report (FR-017). The incarnation is minted when the process first asks for the
/// host id, so it is new each time the process starts (FR-004).
/// </remarks>
internal sealed class InProcessMemberRecord
{
    private static readonly ConcurrentDictionary<string, InProcessMemberRecord> Records = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private PublishedMemberReport? _current;

    private InProcessMemberRecord(ClusterMemberIdentity identity) => Identity = identity;

    public static InProcessMemberRecord ForHost(string hostId) =>
        Records.GetOrAdd(hostId, id => new InProcessMemberRecord(new ClusterMemberIdentity(id, MemberIncarnation.New())));

    public ClusterMemberIdentity Identity { get; }

    /// <summary>The latest published report, or <see langword="null"/> before the first publish.</summary>
    public PublishedMemberReport? Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    /// <summary>Records a report. The revision rises only when the report differs from the one before it.</summary>
    public PublishedMemberReport Publish(MemberReport report)
    {
        lock (_gate)
        {
            if (_current is null || !_current.Report.Equals(report))
                _current = new PublishedMemberReport(Identity, report, (_current?.Revision ?? 0) + 1);
            return _current;
        }
    }
}
