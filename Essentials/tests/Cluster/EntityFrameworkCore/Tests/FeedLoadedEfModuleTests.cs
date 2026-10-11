using System.Runtime.Loader;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Extensions;
using Elsa.Cluster.EntityFrameworkCore.Testing;
using Elsa.Cluster.Testing;
using Elsa.Primitives.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FeedLoadedModuleHost;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// #2143: an EF module that arrives from a feed carries its own copy of <c>Elsa.Persistence.EntityFramework</c> in a load
/// context of its own, and still reaches the membership its host composed on the host container, because both see the
/// types of <c>Elsa.Persistence.Schema</c> and <c>Elsa.Cluster.Core</c>, which every host shares. On
/// <c>Elsa.Foundation.Host</c> and on <c>Elsa.Workbench</c>, from each host's own share list: the module finalizes at
/// activation as a single host (spec 181, FR-021), a feature that needs its new version leaves dormancy once it is
/// finalized (spec 182, FR-019 and SC-003), and a durable provider composed on the host is the fleet the module counts.
/// </summary>
/// <remarks>
/// <para>
/// The dangerous direction is the one that looks healthy: without the share the module still admits and its shell still
/// activates, but its gate never finalizes and its feature is refused as "not observed" for ever. Both are pinned below
/// with the share withheld (the schema one on <c>Elsa.Workbench</c>, for the reason given there), so this suite cannot turn green by loading the fixture where the host's own types are.
/// </para>
/// <para>
/// <c>Elsa.Foundation.Host</c> shares <c>Elsa.Persistence.EntityFramework</c> since #2151, because it carries it for its
/// cluster membership, so there a module never has a copy of its own: the entry's major is the assembly's, whatever
/// build either side is (#2150). <c>Elsa.Workbench</c> carries it without sharing it, so there the module keeps its own.
/// Each host is exercised as it is shipped, and <see cref="AssertLoadedAsAPackage"/> holds the module to that difference.
/// </para>
/// </remarks>
public sealed class FeedLoadedEfModuleTests : IAsyncLifetime
{
    /// <summary>The persistence assembly <c>Elsa.Foundation.Host</c> shares and <c>Elsa.Workbench</c> does not (see the remarks above).</summary>
    private const string Persistence = "Elsa.Persistence.EntityFramework";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);
    private readonly string _file = Path.Join(Path.GetTempPath(), $"elsa-feed-module-{Guid.NewGuid():N}.db");
    private readonly List<IAsyncDisposable> _owned = [];

    public static TheoryData<string> Hosts => [FoundationHost, Workbench];

    private string ConnectionString => $"Data Source={_file};Pooling=False";

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Disposes what the test started before its database is deleted, and deletes it even if disposing failed.</summary>
    public async Task DisposeAsync()
    {
        try
        {
            foreach (var owned in Enumerable.Reverse(_owned))
                await owned.DisposeAsync();
        }
        finally
        {
            DeleteDatabaseFiles(_file);
        }
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task A_feed_loaded_ef_module_finalizes_its_new_version_at_activation_as_a_single_host(string host)
    {
        await SeedAsync(ConnectionString);

        var loaded = await StartAsync(host);

        var record = await RecordAsync(ConnectionString);
        Assert.Equal("2", record.FinalizedVersion);
        var reported = (await loaded.Shell.GetRequiredService<IClusterMembership>().ReadFleetAsync(FleetReadMode.Fresh)).Members
            .Single().Report.Readability!.Entries.Single(entry => entry.Family == Family);
        Assert.Equal((record.DatabaseIdentity, "2"), (reported.DatabaseIdentity, reported.ObservedFinalizedVersion));
        AssertLoadedAsAPackage(host, loaded);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task A_feed_loaded_feature_that_needs_the_new_version_serves_once_it_is_finalized_at_activation(string host)
    {
        await SeedAsync(ConnectionString);

        var loaded = await StartAsync(host);

        Assert.True(Check(loaded).Evaluate(loaded.OrdersRequirements).IsAvailable);
        await loaded.PlaceOrderAsync();
    }

    /// <summary>The same feature leaves dormancy in the running shell, with no restart and no reload, once a hold is released.</summary>
    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task A_feed_loaded_feature_stays_dormant_while_its_version_is_held_and_leaves_dormancy_once_it_is_finalized(string host)
    {
        await SeedAsync(ConnectionString, hold: "canary of 2");
        var loaded = await StartAsync(host);

        Assert.Equal("1", (await RecordAsync(ConnectionString)).FinalizedVersion);
        Assert.Equal(SchemaDormancyKind.Held, Assert.Single(Check(loaded).Evaluate(loaded.OrdersRequirements).Unmet).Kind);
        await Assert.ThrowsAsync<SchemaDormancyRefusedException>(loaded.PlaceOrderAsync);

        await ReleaseHoldAsync(ConnectionString);
        await WaitUntilAsync(() => Check(loaded).Evaluate(loaded.OrdersRequirements).IsAvailable);

        Assert.Equal("2", (await RecordAsync(ConnectionString)).FinalizedVersion);
        await loaded.PlaceOrderAsync();
    }

    /// <summary>
    /// The host composes a durable provider on its container, in which another live member reads only version 1. The
    /// module counts that fleet, not a default of its own: it does not finalize, and its status names the other member.
    /// Once that member reads 2 as well, the module finalizes and the feature serves.
    /// </summary>
    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task A_durable_provider_composed_on_the_host_is_the_fleet_the_feed_loaded_module_counts(string host)
    {
        var fleet = new EfClusterMembershipConformanceFixture(EfClusterMembershipTestStore.CreateSqlite());
        _owned.Add(fleet);
        var self = await fleet.StartMemberAsync(new ConformanceMemberSetup($"self-{Guid.NewGuid():N}", [new ReadabilityEntry(Family, ModuleName, Chain)]));
        var older = await fleet.StartMemberAsync(new ConformanceMemberSetup($"older-{Guid.NewGuid():N}", [new ReadabilityEntry(Family, ModuleName, ["1"])]));
        await SeedAsync(ConnectionString);

        var loaded = await StartAsync(host, services => services.AddClusterMembershipProvider(new ClusterMembershipProviderRegistration(
            "durable-under-test", ClusterProviderKind.Durable, ServiceDescriptor.Singleton(self.Membership))));

        Assert.Equal("1", (await RecordAsync(ConnectionString)).FinalizedVersion);
        Assert.Equal(SchemaDormancyKind.WaitingForHosts, Assert.Single(Check(loaded).Evaluate(loaded.OrdersRequirements).Unmet).Kind);
        var pending = Assert.Single(Assert.Single(await Check(loaded).ReadStatusAsync(), status => status.Family == Family).Pending);
        Assert.Contains(pending.Blockers!, blocker => blocker.Contains(older.Membership.GetLocalStanding().Identity.HostId, StringComparison.Ordinal));

        older.SetReadability(new ReadabilityEntry(Family, ModuleName, Chain));
        await older.Membership.PublishReportAsync();
        await WaitUntilAsync(() => Check(loaded).Evaluate(loaded.OrdersRequirements).IsAvailable);

        Assert.Equal("2", (await RecordAsync(ConnectionString)).FinalizedVersion);
        await loaded.PlaceOrderAsync();
    }

    /// <summary>
    /// Without <c>Elsa.Persistence.Schema</c> shared, the module loads its own copy: it admits and its shell activates, so
    /// nothing fails, but its gate finds no fleet of that type and never finalizes, and the host's dormancy check finds no
    /// gate, so the feature is refused as not observed. This is the failure #2143 closes, and it looks like success. Only
    /// <c>Elsa.Workbench</c> can show it: a module on <c>Elsa.Foundation.Host</c> reaches the schema types through the host's
    /// own <c>Elsa.Persistence.EntityFramework</c>, which holds the host's whatever copy the package carries.
    /// </summary>
    [Fact]
    public async Task Without_the_schema_share_the_module_admits_but_never_finalizes_and_its_feature_reads_not_observed()
    {
        await SeedAsync(ConnectionString);

        var loaded = await StartAsync(Workbench, withheld: "Elsa.Persistence.Schema");
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.Contains(loaded.Package.Assemblies, assembly => assembly.GetName().Name == "Elsa.Persistence.Schema");
        Assert.Equal("1", (await RecordAsync(ConnectionString)).FinalizedVersion);
        Assert.Equal(SchemaDormancyKind.NotObserved, Assert.Single(Check(loaded).Evaluate(loaded.OrdersRequirements).Unmet).Kind);
        await Assert.ThrowsAsync<SchemaDormancyRefusedException>(loaded.PlaceOrderAsync);
    }

    /// <summary>
    /// Without <c>Elsa.Cluster.Core</c> shared, the feature's own check contract is a different type from the one the host
    /// registered its check under, so the feature cannot ask it at all.
    /// </summary>
    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task Without_the_membership_contract_share_the_feed_loaded_feature_cannot_reach_the_hosts_dormancy_check(string host)
    {
        await SeedAsync(ConnectionString);

        var loaded = await StartAsync(host, withheld: "Elsa.Cluster.Core");

        Assert.Contains(loaded.Package.Assemblies, assembly => assembly.GetName().Name == "Elsa.Cluster.Core");
        await Assert.ThrowsAsync<InvalidOperationException>(loaded.PlaceOrderAsync);
    }

    private async Task<FeedLoadedModuleHost> StartAsync(string host, Action<IServiceCollection>? composeMembership = null, params string[] withheld)
    {
        var loaded = await FeedLoadedModuleHost.StartAsync(host, ConnectionString, composeMembership, withheld);
        _owned.Add(loaded);
        return loaded;
    }

    private static ISchemaDormancyCheck Check(FeedLoadedModuleHost loaded) => loaded.Shell.GetRequiredService<ISchemaDormancyCheck>();

    /// <summary>
    /// The module is in the package's load context, the two host-composed contracts are the host's, and so is the EF
    /// persistence where the host shares it: this is the case #2143 is about, not a module compiled into the host.
    /// </summary>
    private static void AssertLoadedAsAPackage(string host, FeedLoadedModuleHost loaded)
    {
        Assert.Same(loaded.Package, AssemblyLoadContext.GetLoadContext(loaded.Module));
        var persistence = loaded.Package.Assemblies.Where(assembly => assembly.GetName().Name == Persistence).ToArray();
        if (host == Workbench)
            Assert.NotSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(Assert.Single(persistence)));
        else
            Assert.Empty(persistence);
        Assert.DoesNotContain(loaded.Package.Assemblies, assembly => assembly.GetName().Name is "Elsa.Persistence.Schema" or "Elsa.Cluster.Core");
    }

    private static Task WaitUntilAsync(Func<bool> condition) => Polling.UntilAsync(condition, Patience, TimeSpan.FromMilliseconds(50));
}
