using System.Net;
using System.Text.Json.Nodes;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.Hosting;
using Elsa.Primitives.Exceptions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FeedLoadedModuleHost;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FoundationHostComposition;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// #2151's acceptance, on the real thing: two built <c>Elsa.Foundation.Host</c> processes that share one SQLite database,
/// each with the EF cluster membership provider enabled from configuration alone, and a feed-loaded EF module whose new
/// schema version one of them runs before the other. They see each other; the new version is finalized only once both can
/// read it; and a feature that needs it stays dormant, with the reason, until then (spec 181, FR-021; spec 182, FR-001 and
/// FR-008; spec 183, FR-023 and FR-024).
/// </summary>
/// <remarks>
/// <para>
/// The dangerous direction is the one that looks healthy: two hosts that do not count each other each finalize on their
/// own, and every request is served. <see cref="Without_the_ef_provider_each_host_is_a_cluster_of_one_and_the_new_version_finalizes_under_the_older_host"/>
/// pins that direction with the provider left off, so the main test's refusals are shown to come from membership, and
/// the main test ends with the version finalized and served, so they are not a gate that never finalizes.
/// </para>
/// <para>
/// Each host is killed rather than stopped, as a crash is: its entry stays live until it expires, so the upgrade shows the
/// new incarnation of the same host id waiting its predecessor out rather than displacing it.
/// </para>
/// </remarks>
public sealed class FoundationHostClusterBootTests(FoundationHostFeed feed) : IClassFixture<FoundationHostFeed>, IAsyncLifetime
{
    private const string Older = "foundation-host-a";
    private const string Newer = "foundation-host-b";

    /// <summary>The upgrade waits out the killed incarnation's expiry, then activates its shell; this covers both.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    /// <summary>How long a version both hosts cannot read is watched staying unfinalized: many gate evaluations and heartbeats.</summary>
    private static readonly TimeSpan Watch = TimeSpan.FromSeconds(4);

    private readonly string _file = Path.Join(Path.GetTempPath(), $"elsa-foundation-host-cluster-{Guid.NewGuid():N}.db");
    private readonly List<FoundationHostProcess> _hosts = [];

    private string ConnectionString => $"Data Source={_file};Pooling=False";

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Stops every host before the database is deleted, and deletes it even if stopping one failed.</summary>
    public async Task DisposeAsync()
    {
        try
        {
            foreach (var host in Enumerable.Reverse(_hosts))
                await host.DisposeAsync();
        }
        finally
        {
            DeleteDatabaseFiles(_file);
        }
    }

    [Fact]
    public async Task Two_hosts_sharing_a_database_finalize_a_feed_loaded_modules_new_version_only_once_both_can_read_it()
    {
        await SeedAsync(ConnectionString);
        var older = await StartAsync(feed.PreviousDirectory, Clustered(Older), EntityFrameworkCoreFeature);
        var newer = await StartAsync(feed.Directory, Clustered(Newer), EntityFrameworkCoreFeature, OrdersFeature);

        // They see each other: both are current members of the one table every fresh read reads, each reporting what it
        // reads of the family.
        await WaitUntilAsync(async () => Describe(await ReadableByMemberAsync()) == Describe(new() { [Older] = ["1"], [Newer] = ["1", "2"] }));

        // The newer host counts the older one: version 2 is not finalized, and the feature that needs it is dormant and
        // says it waits for every host, rather than that it was held or that nothing was observed.
        var deadline = DateTimeOffset.UtcNow + Watch;
        do
        {
            var (status, text) = await OrdersAsync(newer);
            Assert.Equal((HttpStatusCode.Conflict, "1"), (status, (await RecordAsync(ConnectionString)).FinalizedVersion));
            Assert.StartsWith(SchemaDormancyRefusedException.RefusalCode, text, StringComparison.Ordinal);
            Assert.Contains("once every host can read version '2'", text, StringComparison.Ordinal);
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        } while (DateTimeOffset.UtcNow < deadline);

        // The module binds the host's EF closure: neither host acquired EF Core, its engine closure or the EF persistence
        // package from the feed that offers them, only the engine package the ef-provider selection names.
        Assert.All(new[] { older, newer }, AssertBindsTheHostsEfClosure);

        // The older host is upgraded: killed, and started again under the same host id on the new release. Its killed
        // incarnation stays live until it expires, so version 2 is still not finalized the moment it is gone.
        await StopAsync(older);
        Assert.Equal("1", (await RecordAsync(ConnectionString)).FinalizedVersion);
        var upgraded = await StartAsync(feed.Directory, Clustered(Older), EntityFrameworkCoreFeature, OrdersFeature);

        await WaitUntilAsync(async () => (await RecordAsync(ConnectionString)).FinalizedVersion == "2");
        await WaitUntilAsync(async () => (await OrdersAsync(newer)).Status == HttpStatusCode.OK && (await OrdersAsync(upgraded)).Status == HttpStatusCode.OK);
        Assert.Equal(Describe(new() { [Older] = ["1", "2"], [Newer] = ["1", "2"] }), Describe(await ReadableByMemberAsync()));
    }

    /// <summary>
    /// The control: the same two hosts with the provider left off. Each is a cluster of one (spec 183, FR-018a), so the newer
    /// host finalizes version 2 at activation while the older one, which cannot read it, is still running. This is what the
    /// test above shows membership preventing.
    /// </summary>
    [Fact]
    public async Task Without_the_ef_provider_each_host_is_a_cluster_of_one_and_the_new_version_finalizes_under_the_older_host()
    {
        await SeedAsync(ConnectionString);
        var older = await StartAsync(feed.PreviousDirectory, [], EntityFrameworkCoreFeature);
        var newer = await StartAsync(feed.Directory, [], EntityFrameworkCoreFeature, OrdersFeature);

        await WaitUntilAsync(async () => (await RecordAsync(ConnectionString)).FinalizedVersion == "2");

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
        var settings = Clustered(Newer);
        Assert.True(settings.Remove(Key(missing)));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(feed.Directory, settings, EntityFrameworkCoreFeature));

        Assert.Contains(Key(missing), failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Starts a host over <paramref name="packages"/> with <paramref name="membership"/> beside the settings every boot test
    /// gives it, enabling <paramref name="features"/>. It is stopped when the test ends, whatever happens.
    /// </summary>
    private async Task<FoundationHostProcess> StartAsync(string packages, IEnumerable<KeyValuePair<string, string>> membership, params string[] features)
    {
        var settings = Settings(feed);
        foreach (var (key, value) in membership)
            settings[key] = value;

        var host = await FoundationHostProcess.StartAsync(Shells(ConnectionString, features), packages, settings);
        _hosts.Add(host);
        return host;
    }

    private async Task StopAsync(FoundationHostProcess host)
    {
        _hosts.Remove(host);
        await host.DisposeAsync();
    }

    /// <summary>
    /// The EF provider enabled under <paramref name="hostId"/>, over the test's database, with timings short enough that an
    /// upgrade waits seconds for its killed predecessor rather than the default half minute.
    /// </summary>
    private Dictionary<string, string> Clustered(string hostId) => new()
    {
        [Key(nameof(ClusterMembershipOptions.HostId))] = hostId,
        [Key(nameof(ClusterMembershipOptions.HeartbeatInterval))] = "00:00:00.500",
        [Key(nameof(ClusterMembershipOptions.ExpiryPeriod))] = "00:00:05",
        [Key(nameof(ClusterMembershipOptions.SkewAllowance))] = "00:00:01",
        [Key(EfKey(ClusterMembershipConfigurationExtensions.EnabledKey))] = "true",
        [Key(EfKey(nameof(EfClusterMembershipOptions.Provider)))] = "Sqlite",
        [Key(EfKey(nameof(EfClusterMembershipOptions.ConnectionString)))] = ConnectionString,
        [Key(EfKey(nameof(EfClusterMembershipOptions.CleanupPeriod)))] = "00:01:00"
    };

    private static string Key(string name) => $"{ClusterMembershipOptions.SectionName}:{name}";

    private static string EfKey(string name) => $"{EfClusterMembershipOptions.SectionKey}:{name}";

    /// <summary>
    /// Every current member of the membership table that has not left, by host id, with the versions of the fixture's family
    /// its latest report says it reads: the fleet a fresh read returns to either host.
    /// </summary>
    private async Task<Dictionary<string, string[]>> ReadableByMemberAsync()
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

    private static string[] ReadableVersions(string reportJson)
    {
        var entries = JsonNode.Parse(reportJson)?["readability"]?["entries"]?.AsArray() ?? [];
        return
        [
            .. entries
                .Where(entry => entry?["family"]?.GetValue<string>() == Family)
                .SelectMany(entry => entry!["readableVersions"]!.AsArray().Select(version => version!.GetValue<string>()))
        ];
    }

    private static string Describe(Dictionary<string, string[]> readable) =>
        string.Join("; ", readable.OrderBy(member => member.Key, StringComparer.Ordinal).Select(member => $"{member.Key} reads [{string.Join(", ", member.Value)}]"));

    /// <summary>
    /// What the host acquired from its feeds holds no second copy of anything it carries for its membership: no Elsa
    /// package but the fixture, no EF Core package but the engine package the capability selection names (for SQLite a
    /// package with no assembly of its own, whose dependencies are all the host's), and no SQLite driver or native library.
    /// The framework packages the closure feed also offers may be acquired, but are never loaded: the runtime carries them.
    /// </summary>
    private static void AssertBindsTheHostsEfClosure(FoundationHostProcess host)
    {
        const string fixture = "Elsa.Cluster.Fixtures.FeedModule", engine = "Microsoft.EntityFrameworkCore.Sqlite";
        string[] carried = ["Elsa.", "Microsoft.EntityFrameworkCore", "Microsoft.Data.Sqlite", "SQLitePCLRaw."];
        var active = host.ActivePackages().Keys.ToArray();

        Assert.Contains(fixture, active, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(engine, active, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(active
            .Where(id => carried.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Except([fixture, engine], StringComparer.OrdinalIgnoreCase));
    }

    private async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (!await condition())
        {
            Assert.True(DateTimeOffset.UtcNow < deadline,
                $"Not met within {Patience}. Members: {Describe(await ReadableByMemberAsync())}. " +
                string.Concat(_hosts.Select((host, index) => $"{Environment.NewLine}Host {index} output:{Environment.NewLine}{host.Output}")));
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
