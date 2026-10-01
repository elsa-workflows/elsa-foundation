using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Cluster.Readability.Tests;

/// <summary>
/// #2099's acceptance through a host that composes nothing but <see cref="EfSchemaReadabilityServiceCollectionExtensions.AddEfSchemaReadability"/>:
/// it is a cluster of one whose report carries the families it has loaded, so the counting query of spec 183's FR-023
/// answers "can every counted member read family F at version V?" from what the host actually loaded.
/// </summary>
public sealed class ReadabilityMembershipTests : IAsyncDisposable
{
    private const string Family = RuntimeArtifactEfModule.SchemaFamily;
    private const string CurrentVersion = RuntimeArtifactEfModule.SchemaVersion;
    private const string NewerVersion = "2.0.0";

    private readonly ServiceProvider _host;
    private readonly IClusterMembership _membership;

    public ReadabilityMembershipTests()
    {
        // The module's constants compile into this assembly, so name one of its types to load it into the host.
        _ = typeof(RuntimeDbContext).Assembly;

        var services = new ServiceCollection().Configure<ClusterMembershipOptions>(options => options.HostId = $"readability-{Guid.NewGuid():N}");
        _host = services.AddEfSchemaReadability().BuildServiceProvider();
        _membership = _host.GetRequiredService<IClusterMembership>();
    }

    [Fact]
    public async Task A_host_that_composes_readability_is_a_cluster_of_one_reporting_the_families_it_has_loaded()
    {
        var self = Assert.Single((await _membership.ReadFleetAsync(FleetReadMode.Fresh)).Members);

        Assert.Equal(ClusterProviderKind.InProcess, _membership.ProviderKind);
        Assert.Equal(new ReadabilityEntry(Family, "Workflows.Runtime", [CurrentVersion], moduleActive: false), Entry(self));
    }

    [Fact]
    public async Task Every_counted_member_can_read_a_loaded_family_at_its_current_version()
    {
        var answer = await CountAsync(CurrentVersion);

        Assert.True(answer.EveryConsideredMemberMatches);
        Assert.Single(answer.Matches);
    }

    /// <summary>
    /// The direction that could pass for success: were the family missing from the report, this host would not be
    /// counted for it and the answer would be yes.
    /// </summary>
    [Fact]
    public async Task A_newer_version_of_a_loaded_family_is_blocked_by_this_host_with_what_it_reads()
    {
        var answer = await CountAsync(NewerVersion);

        Assert.False(answer.EveryConsideredMemberMatches);
        var blocker = Assert.Single(answer.Failures);
        Assert.Equal(new ReadsSchemaVersion(Family, NewerVersion), blocker.Requirement);
        Assert.Equal([CurrentVersion], Entry(blocker.Member).ReadableVersions);
    }

    [Fact]
    public async Task A_family_this_host_has_not_loaded_does_not_count_it()
    {
        var answer = await _membership.QueryAsync(MemberQuery.Counting(new ReadsSchemaVersion($"NotLoaded{Guid.NewGuid():N}", NewerVersion)), FleetReadMode.Fresh);

        Assert.True(answer.EveryConsideredMemberMatches);
    }

    [Fact]
    public async Task Composing_readability_twice_registers_one_source()
    {
        await using var host = new ServiceCollection()
            .Configure<ClusterMembershipOptions>(options => options.HostId = $"readability-{Guid.NewGuid():N}")
            .AddEfSchemaReadability()
            .AddEfSchemaReadability()
            .BuildServiceProvider();

        Assert.IsType<EfSchemaReadabilitySource>(Assert.Single(host.GetServices<IMemberReportSource<ReadabilitySection>>()));
        Assert.Contains(Family, (await host.GetRequiredService<IClusterMembership>().PublishReportAsync()).Report.Readability?.Entries.Select(entry => entry.Family) ?? []);
    }

    /// <summary>
    /// Spec 186, FR-012: the fleet a host composes gives the backfill's default settle margin, the membership expiry
    /// period plus the skew allowance, as the membership settings say when it is asked.
    /// </summary>
    [Fact]
    public async Task The_fleets_settle_margin_is_the_membership_expiry_period_plus_the_skew_allowance()
    {
        await using var host = new ServiceCollection()
            .Configure<ClusterMembershipOptions>(options =>
            {
                options.HostId = $"readability-{Guid.NewGuid():N}";
                options.ExpiryPeriod = TimeSpan.FromSeconds(40);
                options.SkewAllowance = TimeSpan.FromSeconds(7);
            })
            .AddEfSchemaReadability()
            .BuildServiceProvider();

        Assert.Equal(TimeSpan.FromSeconds(47), host.GetRequiredService<IEfSchemaFleet>().SettleMargin);
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    private ValueTask<MemberQueryAnswer> CountAsync(string version) =>
        _membership.QueryAsync(MemberQuery.Counting(new ReadsSchemaVersion(Family, version)), FleetReadMode.Fresh);

    private static ReadabilityEntry Entry(FleetMember member)
    {
        var readability = member.Report.Readability;
        Assert.NotNull(readability);
        return Assert.Single(readability.Entries, entry => entry.Family == Family);
    }
}
