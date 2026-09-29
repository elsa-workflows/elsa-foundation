using System.Net;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FeedLoadedModuleHost;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FoundationHostComposition;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// #2143's acceptance, on the real thing: the built <c>Elsa.Foundation.Host</c> as a child process, with the feed-module
/// fixture packed into a directory feed, and Nuplane loading it as it loads any package. Nothing here composes the host,
/// its membership or the module's load context: what <see cref="FeedLoadedEfModuleTests"/> assembles in process, the host
/// assembles itself, from its own shipped <c>appsettings.json</c> shares and its own <c>Program.cs</c>. With no membership
/// configured, the host is a cluster of one on the in-process default.
/// </summary>
/// <remarks>
/// The host carries EF Core, the four engines and <c>Elsa.Persistence.EntityFramework</c> for its opt-in cluster membership
/// (#2151), and the module binds those copies. Its feeds still offer EF Core and the Sqlite engine's closure, taken by
/// <see cref="FoundationHostFeed"/> from the package cache that restoring this project filled, so the run needs no network
/// and a host that acquired a second copy could; <see cref="FoundationHostClusterBootTests"/> asserts it does not.
/// </remarks>
public sealed class FoundationHostBootTests(FoundationHostFeed feed) : IClassFixture<FoundationHostFeed>, IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);
    private readonly string _file = Path.Join(Path.GetTempPath(), $"elsa-foundation-host-boot-{Guid.NewGuid():N}.db");
    private FoundationHostProcess? _host;

    private string ConnectionString => $"Data Source={_file};Pooling=False";

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Stops the host before its database is deleted, and deletes it even if stopping the host failed.</summary>
    public async Task DisposeAsync()
    {
        try
        {
            if (_host is not null)
                await _host.DisposeAsync();
        }
        finally
        {
            DeleteDatabaseFiles(_file);
        }
    }

    /// <summary>
    /// The module finalizes its new version at activation as a single host (spec 181, FR-021) and its feature serves at
    /// once (spec 182, FR-019): the record's finalized version moves from 1 to 2 with no operator and no restart.
    /// </summary>
    [Fact]
    public async Task A_feed_loaded_ef_module_finalizes_its_new_version_at_activation_and_its_feature_serves()
    {
        await SeedAsync(ConnectionString);

        await StartAsync();

        await WaitUntilAsync(async () => (await RecordAsync(ConnectionString)).FinalizedVersion == "2");
        Assert.Equal(HttpStatusCode.OK, (await Orders()).Status);
    }

    /// <summary>
    /// While a hold is on the version the feature needs, the host keeps it dormant and says why; releasing the hold on the
    /// database leaves dormancy in the running host, with no restart and no reload.
    /// </summary>
    [Fact]
    public async Task A_feed_loaded_feature_stays_dormant_while_its_version_is_held_and_serves_once_the_hold_is_released()
    {
        await SeedAsync(ConnectionString, hold: "canary of 2");

        await StartAsync();

        var (status, body) = await Orders();
        Assert.Equal((HttpStatusCode.Conflict, "1"), (status, (await RecordAsync(ConnectionString)).FinalizedVersion));
        Assert.Contains("held", body, StringComparison.OrdinalIgnoreCase);

        await ReleaseHoldAsync(ConnectionString);

        await WaitUntilAsync(async () => (await Orders()).Status == HttpStatusCode.OK);
        Assert.Equal("2", (await RecordAsync(ConnectionString)).FinalizedVersion);
    }

    private async Task StartAsync() => _host = await FoundationHostProcess.StartAsync(
        Shells(ConnectionString, EntityFrameworkCoreFeature, OrdersFeature),
        feed.Directory,
        Settings(feed));

    private Task<(HttpStatusCode Status, string Text)> Orders() => OrdersAsync(_host!);

    private async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (!await condition())
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, $"Not met within {Patience}. Host output:{Environment.NewLine}{_host!.Output}");
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}
