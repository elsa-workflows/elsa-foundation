using System.Collections.Concurrent;
using Elsa.Cluster.Core.Models;
using Microsoft.Extensions.Primitives;

namespace Elsa.Cluster.InProcess;

/// <summary>
/// The in-process member of this process under one host id: its incarnation and its latest published report.
/// </summary>
/// <remarks>
/// <para>
/// A shell container is built from copies of the host's registrations, so each shell constructs its own
/// <see cref="InProcessClusterMembership"/>. Keeping the member here, once per process, is what makes every shell see
/// the same incarnation and the same report (FR-017). The incarnation is minted when the process first asks for the
/// host id, so it is new each time the process starts (FR-004).
/// </para>
/// <para>
/// The <see cref="Records"/> dictionary is process-wide by design (FR-017: one member per process, shared by every
/// shell that host id resolves to). It is not scoped to a container or a shell. Two logical hosts running in the same
/// process MUST be configured with distinct host ids; giving them the same one makes them share one incarnation and
/// one report here, which is indistinguishable from a bug.
/// </para>
/// </remarks>
internal sealed class InProcessMemberRecord
{
    private static readonly ConcurrentDictionary<string, InProcessMemberRecord> Records = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private PublishedMemberReport? _current;
    private CancellationTokenSource _changeSource = new();

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

    /// <summary>Records a report. The revision rises only when the report differs from the one before it, in which
    /// case the change token from <see cref="GetChangeToken"/> fires (FR-013).</summary>
    public PublishedMemberReport Publish(MemberReport report)
    {
        lock (_gate)
        {
            if (_current is null || !_current.Report.Equals(report))
            {
                _current = new PublishedMemberReport(Identity, report, (_current?.Revision ?? 0) + 1);
                var previous = _changeSource;
                _changeSource = new CancellationTokenSource();
                previous.Cancel();
            }

            return _current;
        }
    }

    /// <summary>A change token that completes once, the next time this member's published report changes (FR-013).
    /// Call this again after it completes to observe the next change.</summary>
    public IChangeToken GetChangeToken()
    {
        lock (_gate)
            return new CancellationChangeToken(_changeSource.Token);
    }
}
