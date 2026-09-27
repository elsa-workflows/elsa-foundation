using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Cluster.InProcess;

/// <summary>
/// The default membership provider: a cluster of one, the host itself (FR-017, FR-018).
/// </summary>
/// <remarks>
/// Its fleet view is this host alone, live and active. Fresh and cached reads are the same, a publish is visible at
/// once, and the member never lapses. It writes nothing durable and runs no heartbeat, hosted service or timer; its
/// only state is the process's <see cref="InProcessMemberRecord"/>. Its host id is the configured one or, by default,
/// the machine name (spec 183, Decisions, Q24).
/// </remarks>
public sealed class InProcessClusterMembership : IClusterMembership
{
    private readonly InProcessMemberRecord _record;
    private readonly TimeSpan _expiryPeriod;
    private readonly IMemberReportSource<ReadabilitySection>? _readability;
    private readonly TimeProvider _timeProvider;

    public InProcessClusterMembership(
        IOptions<ClusterMembershipOptions> options,
        IEnumerable<IMemberReportSource<ReadabilitySection>> readabilitySources,
        TimeProvider timeProvider)
    {
        var settings = options.Value;
        _record = InProcessMemberRecord.ForHost(
            ClusterHostIdConstraints.Validate(settings.HostId ?? Environment.MachineName, nameof(settings.HostId)));
        _expiryPeriod = settings.ExpiryPeriod;
        _readability = MemberReportComposition.SingleSource(readabilitySources);
        _timeProvider = timeProvider;
    }

    public ClusterProviderKind ProviderKind => ClusterProviderKind.InProcess;

    public LocalMemberStanding GetLocalStanding() => new(_record.Identity, MemberStatus.Active, Lapse: null);

    public async ValueTask<FleetView> ReadFleetAsync(FleetReadMode mode, CancellationToken cancellationToken = default)
    {
        var published = _record.Current ?? await PublishReportAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var self = new FleetMember(
            _record.Identity,
            MemberStatus.Active,
            LastHeartbeatAt: now,
            _expiryPeriod,
            IsLive: true,
            IsDisplaced: false,
            published.Report,
            published.Revision,
            Conditions: []);

        return new FleetView(ClusterProviderKind.InProcess, mode, now, [self]);
    }

    public async ValueTask<PublishedMemberReport> PublishReportAsync(CancellationToken cancellationToken = default) =>
        _record.Publish(await MemberReportComposition.ComposeAsync(_readability, cancellationToken));

    public async ValueTask<MemberQueryAnswer> QueryAsync(MemberQuery query, FleetReadMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Evaluate(await ReadFleetAsync(mode, cancellationToken));
    }
}
