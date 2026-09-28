using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Cluster.Testing;

/// <summary>
/// What a membership provider supplies to run <see cref="ClusterMembershipConformanceTests"/> (FR-044): members it can
/// start, stop, kill, isolate and restart, over one store, on clocks the suite controls.
/// </summary>
/// <remarks>
/// One fixture is one fleet over one fresh store. Every member runs with <see cref="Timings"/>. A member is restarted by
/// starting another one under the same host id after the first was killed or stopped. Operations a cluster of one
/// cannot perform throw <see cref="NotSupportedException"/>; the tests that need them report themselves not applicable
/// when <see cref="SupportsMultipleMembers"/> is <see langword="false"/>.
/// </remarks>
public interface IClusterMembershipConformanceFixture : IAsyncDisposable
{
    /// <summary>The name the provider registers under.</summary>
    string ProviderName { get; }

    ClusterProviderKind ProviderKind { get; }

    /// <summary><see langword="false"/> for a cluster of one.</summary>
    bool SupportsMultipleMembers { get; }

    /// <summary>The heartbeat interval, expiry period and skew allowance every member runs with.</summary>
    ConformanceTimings Timings { get; }

    /// <summary>How long a left or expired entry is kept before cleanup may delete it, or <see langword="null"/> when the
    /// provider stores nothing to clean up.</summary>
    TimeSpan? CleanupPeriod { get; }

    /// <summary>
    /// Composes the provider on a host container through the provider's own composition entry point, exactly as a host
    /// selects it. A provider that joins does so while the host starts.
    /// </summary>
    void ComposeProvider(IServiceCollection services);

    /// <summary>How many entries the provider's store holds; always zero for a provider that stores nothing.</summary>
    ValueTask<int> CountStoredEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts a member: it joins and becomes active.</summary>
    ValueTask<IConformanceMember> StartMemberAsync(ConformanceMemberSetup setup, CancellationToken cancellationToken = default);

    /// <summary>Joins a member and leaves it joining, as a host that has not finished starting.</summary>
    ValueTask<IConformanceMember> JoinMemberAsync(ConformanceMemberSetup setup, CancellationToken cancellationToken = default);

    /// <summary>
    /// Advances every member's clock by <paramref name="delta"/>. When it returns, every running member has performed
    /// every heartbeat that fell due, in order; a killed member performs none and an isolated one fails each.
    /// </summary>
    ValueTask AdvanceAsync(TimeSpan delta, CancellationToken cancellationToken = default);

    /// <summary>Writes an entry for <paramref name="hostId"/> that no reader can interpret, as a newer provider version
    /// would, heartbeating now.</summary>
    ValueTask PlantUninterpretableEntryAsync(string hostId, CancellationToken cancellationToken = default);

    /// <summary>Runs the provider's cleanup of left and expired entries once, at the current time.</summary>
    ValueTask RunCleanupAsync(CancellationToken cancellationToken = default);
}

/// <summary>One member of a conformance fleet.</summary>
public interface IConformanceMember
{
    IClusterMembership Membership { get; }

    /// <summary>The member's clock. Clocks of different members may be offset from each other.</summary>
    TimeProvider Clock { get; }

    /// <summary>Sets what the member's readability source reports from its next publish.</summary>
    void SetReadability(params ReadabilityEntry[] entries);

    /// <summary>The host finished starting: joining becomes active.</summary>
    ValueTask ActivateAsync();

    /// <summary>The host began to stop: the member drains.</summary>
    ValueTask BeginStopAsync();

    /// <summary>The host stopped gracefully: the member leaves.</summary>
    ValueTask StopAsync();

    /// <summary>The process dies: no further heartbeat and nothing written.</summary>
    ValueTask KillAsync();

    /// <summary>The process keeps running but can no longer reach the store.</summary>
    ValueTask IsolateAsync();
}

/// <summary>How to start a member. <paramref name="Readability"/> <see langword="null"/> means the member has no
/// readability source at all.</summary>
public sealed record ConformanceMemberSetup(string HostId, IReadOnlyList<ReadabilityEntry>? Readability, TimeSpan ClockOffset = default);

/// <summary>The liveness settings a conformance fleet runs with.</summary>
public sealed record ConformanceTimings(TimeSpan HeartbeatInterval, TimeSpan ExpiryPeriod, TimeSpan SkewAllowance);
