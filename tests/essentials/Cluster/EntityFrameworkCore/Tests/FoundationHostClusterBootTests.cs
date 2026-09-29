using System.Net;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FeedLoadedModuleHost;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FoundationHostComposition;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// #2151's acceptance on SQLite, which serves several processes on one machine (spec 183, FR-033): the scenario of
/// <see cref="FoundationHostClusterScenario"/> over one database file, and the two controls that only make sense there.
/// </summary>
/// <remarks>
/// The dangerous direction is the one that looks healthy: two hosts that do not count each other each finalize on their
/// own, and every request is served. <see cref="Without_the_ef_provider_each_host_is_a_cluster_of_one_and_the_new_version_finalizes_under_the_older_host"/>
/// pins that direction with the provider left off, so the scenario's refusals are shown to come from membership, and the
/// scenario ends with the version finalized and served, so they are not a gate that never finalizes.
/// </remarks>
[Collection(FoundationHostCollection.Name)]
public sealed class FoundationHostClusterBootTests(FoundationHostFeed feed) : FoundationHostClusterScenario(feed, "Sqlite")
{
    private readonly string _file = Path.Join(Path.GetTempPath(), $"elsa-foundation-host-cluster-{Guid.NewGuid():N}.db");

    protected override string ConnectionString => $"Data Source={_file};Pooling=False";

    private protected override FeedModuleDatabase Database => new((builder, connectionString) => builder.UseSqlite(connectionString), ConnectionString);

    /// <summary>Stops every host before the database is deleted, and deletes it even if stopping one failed.</summary>
    public override async Task DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            DeleteDatabaseFiles(_file);
        }
    }

    /// <summary>
    /// The control: the same two hosts with the provider left off. Each is a cluster of one (spec 183, FR-018a), so the newer
    /// host finalizes version 2 at activation while the older one, which cannot read it, is still running. This is what the
    /// scenario shows membership preventing.
    /// </summary>
    [Fact]
    public async Task Without_the_ef_provider_each_host_is_a_cluster_of_one_and_the_new_version_finalizes_under_the_older_host()
    {
        await Database.SeedAsync();
        var older = await StartAsync(Feed.PreviousDirectory, [], EntityFrameworkCoreFeature);
        var newer = await StartAsync(Feed.Directory, [], EntityFrameworkCoreFeature, OrdersFeature);

        await WaitUntilAsync(async () => (await Database.RecordAsync()).FinalizedVersion == "2");

        Assert.Equal(HttpStatusCode.OK, (await OrdersAsync(newer)).Status);
        Assert.Equal(HttpStatusCode.OK, (await older.GetAsync("/health/live")).Status);
        Assert.Empty(await ReadableByMemberAsync());
    }

    /// <summary>
    /// A host that meant to join a cluster and silently stayed a cluster of one is the failure that looks like success, so
    /// both half-configured shapes refuse to start, naming the key to set (spec 183, FR-003a; FR-018a).
    /// </summary>
    [Theory]
    [InlineData(nameof(ClusterMembershipOptions.HostId))]
    [InlineData($"{EfClusterMembershipOptions.SectionKey}:{ClusterMembershipConfigurationExtensions.EnabledKey}")]
    public async Task A_host_whose_ef_provider_is_half_configured_refuses_to_start(string missing)
    {
        // Seeded, so a host that did not refuse would start in seconds and this would fail at once rather than time out.
        await Database.SeedAsync();
        var settings = Clustered(Newer);
        Assert.True(settings.Remove(Key(missing)));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(Feed.Directory, settings, EntityFrameworkCoreFeature));

        Assert.Contains(Key(missing), failure.Message, StringComparison.Ordinal);
    }

    protected override async Task<Dictionary<string, string[]>> ReadableByMemberAsync()
    {
        await using var context = new ClusterMembershipSqliteDbContext(
            new DbContextOptionsBuilder<ClusterMembershipSqliteDbContext>().UseSqlite(ConnectionString).Options);
        try
        {
            return (await context.Members.AsNoTracking().Where(member => member.CurrentHostId != null && member.LeftAtUtcTicks == null).ToListAsync())
                .ToDictionary(member => member.HostId, member => ReadableVersions(member.ReportJson));
        }
        catch (SqliteException exception) when (exception.Message.Contains("no such table", StringComparison.Ordinal))
        {
            return []; // No host has composed the provider, so none has created its table.
        }
    }

    /// <summary>
    /// What the host acquired from its feeds holds no second copy of anything it carries for its membership: no Elsa
    /// package but the fixture, no EF Core package but the engine package the capability selection names (for SQLite a
    /// package with no assembly of its own, whose dependencies are all the host's), and no SQLite driver or native library.
    /// The framework packages the closure feed also offers may be acquired, but are never loaded: the runtime carries them.
    /// </summary>
    private protected override async Task AssertBindsTheHostsEfClosureAsync(FoundationHostProcess host)
    {
        const string fixture = "Elsa.Cluster.Fixtures.FeedModule", engine = "Microsoft.EntityFrameworkCore.Sqlite";
        string[] carried = ["Elsa.", "Microsoft.EntityFrameworkCore", "Microsoft.Data.Sqlite", "SQLitePCLRaw."];
        var active = (await host.ActivePackagesAsync()).Keys.ToArray();

        Assert.Contains(fixture, active, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(engine, active, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(active
            .Where(id => carried.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Except([fixture, engine], StringComparer.OrdinalIgnoreCase));
    }
}
