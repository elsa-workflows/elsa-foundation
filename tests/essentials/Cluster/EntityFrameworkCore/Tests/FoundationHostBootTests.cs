using System.Net;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FeedLoadedModuleHost;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// #2143's acceptance, on the real thing: the built <c>Elsa.Foundation.Host</c> as a child process, with the feed-module
/// fixture and the EF persistence package it carries a private copy of packed into a directory feed, and Nuplane loading
/// them as it loads any package. Nothing here composes the host, its membership or the module's load context: what
/// <see cref="FeedLoadedEfModuleTests"/> assembles in process, the host assembles itself, from its own shipped
/// <c>appsettings.json</c> shares and its own <c>Program.cs</c>.
/// </summary>
/// <remarks>
/// The host carries no EF Core (ADR 0076), so its feeds also have to supply Microsoft.EntityFrameworkCore, the provider
/// engine (chosen by the <c>ef-provider</c> capability the fixture's <c>nuplane.json</c> declares) and the rest of their
/// closure. <see cref="FoundationHostFeed"/> takes those from the package cache that restoring this project filled, so the
/// run needs no network.
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
        Shells(ConnectionString),
        feed.Directory,
        new Dictionary<string, string>
        {
            // A second feed beside the host's own `packages` one, which resolves what the packages there depend on and holds
            // nothing the host loads on its own account: with no include patterns it is a source, not a list of roots.
            ["Nuplane:Setup:Feeds:1:Name"] = "closure",
            ["Nuplane:Setup:Feeds:1:DirectoryPath"] = feed.ClosureDirectory,
            // The one engine the module's ef-provider capability offers that this database uses.
            ["Nuplane:Capabilities:ef-provider"] = "Sqlite",
            // The fixture ships no migrations: its database is created by the test, as a release before it did.
            [$"{EfMigrateOptions.SectionName}:{nameof(EfMigrateOptions.Policy)}"] = nameof(EfMigratePolicy.Validate),
            [$"{EfSchemaFinalizationOptions.SectionName}:{nameof(EfSchemaFinalizationOptions.EvaluationInterval)}"] = "00:00:00.200",
            [$"{EfSchemaFinalizationOptions.SectionName}:{nameof(EfSchemaFinalizationOptions.RefreshInterval)}"] = "00:00:00.100"
        });

    private Task<(HttpStatusCode Status, string Body)> Orders() => _host!.GetAsync("/feed-module-fixture/orders");

    private static string Shells(string connectionString) => new JsonObject
    {
        ["CShells"] = new JsonObject
        {
            ["Shells"] = new JsonObject
            {
                ["default"] = new JsonObject
                {
                    ["Name"] = "default",
                    ["Features"] = new JsonObject
                    {
                        ["FeedModuleFixtureEntityFrameworkCore"] = new JsonObject { ["ConnectionString"] = connectionString },
                        ["FeedModuleFixtureOrders"] = new JsonObject()
                    },
                    ["Configuration"] = new JsonObject { ["WebRouting"] = new JsonObject { ["Path"] = "" } }
                }
            }
        }
    }.ToJsonString();

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
