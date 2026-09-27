using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Elsa.Cluster.Testing;

/// <summary>
/// The provider-neutral cluster membership conformance suite (spec 183, FR-044). A provider is supported only once a
/// test class deriving from this one, with the provider's fixture, passes (ADR 0078). Each test is named after the
/// requirement it proves, so a failure names exactly which one broke.
/// </summary>
/// <remarks>
/// <para>
/// The membership tier (FR-045 to FR-055, with FR-004b, FR-011 and FR-023 beside them) proves the semantics of the
/// fleet view. The invariant tier (FR-056 and FR-058, with FR-001, FR-003a and FR-017 behind invariant 1) proves ADR
/// 0078's invariants. FR-057 and FR-059 prove invariants 2 and 4 over the distributed runtime composed on the provider;
/// they join this suite when placement consumes membership (B7, #2103).
/// </para>
/// <para>
/// A test that needs more than one member reports itself not applicable for a cluster of one.
/// </para>
/// </remarks>
public abstract partial class ClusterMembershipConformanceTests(IClusterMembershipConformanceFixture fixture) : IAsyncDisposable
{
    protected const string NotApplicableToClusterOfOne = "Not applicable to a cluster of one.";

    /// <summary>The schema family the readability tests report on.</summary>
    protected const string Family = "conformance-family";

    protected IClusterMembershipConformanceFixture Fixture { get; } = fixture;

    protected ConformanceTimings Timings => Fixture.Timings;

    /// <summary>
    /// How long after its last heartbeat a killed member has stopped heartbeating, so a restart may displace it while
    /// it is still counted: one heartbeat interval and the skew allowance, and a tenth of an interval more.
    /// </summary>
    protected TimeSpan DisplacementDelay => Timings.HeartbeatInterval + Timings.SkewAllowance + Timings.HeartbeatInterval / 10;

    /// <summary>A margin well above any store's timestamp precision, used on either side of a boundary.</summary>
    protected static TimeSpan Margin => TimeSpan.FromMilliseconds(1);

    public async ValueTask DisposeAsync()
    {
        await Fixture.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    protected void RequireMultipleMembers() => Skip.IfNot(Fixture.SupportsMultipleMembers, NotApplicableToClusterOfOne);

    protected static string NewHostId(string name) => $"{name}-{Guid.NewGuid():N}"[..(name.Length + 9)];

    protected static ReadabilityEntry Reads(params string[] versions) => new(Family, "ConformanceModule", versions);

    protected static ConformanceMemberSetup Setup(string name, params ReadabilityEntry[] reads) => new(NewHostId(name), reads);

    protected static ConformanceMemberSetup Restart(IConformanceMember member, params ReadabilityEntry[] reads) => new(Identity(member).HostId, reads);

    protected static ClusterMemberIdentity Identity(IConformanceMember member) => member.Membership.GetLocalStanding().Identity;

    protected static async Task<FleetView> FreshViewAsync(IConformanceMember reader) => await reader.Membership.ReadFleetAsync(FleetReadMode.Fresh);

    /// <summary>The entry <paramref name="reader"/> sees for <paramref name="identity"/> in a fresh read, or
    /// <see langword="null"/> when its view does not hold it.</summary>
    protected static async Task<FleetMember?> SeenAsync(IConformanceMember reader, ClusterMemberIdentity identity) =>
        (await FreshViewAsync(reader)).Find(identity);

    protected static async Task<MemberQueryAnswer> AskAsync(IConformanceMember reader, MemberQuery query) =>
        await reader.Membership.QueryAsync(query, FleetReadMode.Fresh);

    /// <summary>Advances every clock until <paramref name="reader"/>'s clock shows <paramref name="instant"/>.</summary>
    protected async Task AdvanceUntilAsync(IConformanceMember reader, DateTimeOffset instant)
    {
        var delta = instant - reader.Clock.GetUtcNow();
        Assert.True(delta >= TimeSpan.Zero, $"Cannot move the clock back to {instant:O}.");
        await Fixture.AdvanceAsync(delta);
    }

    /// <summary>The instant on <paramref name="reader"/>'s clock after which <paramref name="subject"/> may be judged
    /// expired, from the reader's own fresh view of it.</summary>
    protected async Task<DateTimeOffset> ExpiryAsSeenByAsync(IConformanceMember reader, ClusterMemberIdentity subject)
    {
        var entry = await SeenAsync(reader, subject);
        Assert.NotNull(entry);
        return MemberLiveness.ExpiresAfter(entry.LastHeartbeatAt, entry.ExpiryPeriod, Timings.SkewAllowance);
    }

    protected static void AssertIdentities(IEnumerable<ClusterMemberIdentity> expected, IEnumerable<FleetMember> actual) =>
        Assert.Equal(
            expected.Select(identity => identity.ToString()).Order(StringComparer.Ordinal),
            actual.Select(member => member.Identity.ToString()).Order(StringComparer.Ordinal));

    /// <summary>
    /// Builds and starts a host with the given compositions on its host container, the way a host selects its
    /// provider. Composition errors and the startup check surface from here.
    /// </summary>
    protected static async Task<ConformanceHost> StartHostAsync(string? hostId, params Action<IServiceCollection>[] compositions)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        if (hostId is not null)
            builder.Services.Configure<ClusterMembershipOptions>(options => options.HostId = hostId);
        foreach (var compose in compositions)
            compose(builder.Services);

        var host = builder.Build();
        try
        {
            await host.StartAsync();
            return new ConformanceHost(host, builder.Services);
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    /// <summary>A started host and the host container's registrations.</summary>
    protected sealed record ConformanceHost(IHost Host, IServiceCollection Registrations) : IDisposable
    {
        public IServiceProvider Services => Host.Services;

        /// <summary>A container built from copies of the host's registrations, as CShells builds each shell.</summary>
        public ServiceProvider BuildShellContainer()
        {
            IServiceCollection shell = new ServiceCollection();
            foreach (var descriptor in Registrations)
                shell.Add(descriptor);
            return shell.BuildServiceProvider();
        }

        public void Dispose() => Host.Dispose();
    }
}
