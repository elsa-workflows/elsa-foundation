using System.Net;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Microsoft.Extensions.Configuration;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FeedLoadedModuleHost;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// #2143's acceptance, on the real thing: the built <c>Elsa.Foundation.Host</c> as a child process, with the feed-module
/// fixture and the EF persistence package it carries a private copy of packed into a directory feed, and Nuplane loading
/// them as it loads any package. Nothing here composes the host, its membership or the module's load context: what
/// <see cref="FeedLoadedEfModuleTests"/> assembles in process, the host assembles itself, from its own shipped
/// <c>appsettings.json</c> shares and its own <c>Program.cs</c>. And #2150's: a package that carries its own copies of the
/// assemblies the host shares binds the host's, because Nuplane binds, validates and matches those shipped shares itself.
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

    /// <summary>
    /// #2150: the fixture's package carries its own copies of <c>Elsa.Persistence.Schema</c> and <c>Elsa.Cluster.Core</c>,
    /// and Nuplane, which loads every assembly of a host-integrated graph unless the host's shared-assembly policy matches
    /// it, takes both for the host's shares on the name, token and major the host declares. So the module binds the host's
    /// copies: it finalizes at activation and its feature serves, exactly as when it carries none.
    /// </summary>
    [Fact]
    public async Task A_feed_package_carrying_its_own_copies_of_shared_assemblies_binds_the_hosts_and_finalizes()
    {
        await SeedAsync(ConnectionString);

        await StartAsync(feed.CarryingDirectory);

        Assert.All(FoundationHostFeed.CarriedShares, share => Assert.Single(Carried(share)));
        await WaitUntilAsync(async () => (await RecordAsync(ConnectionString)).FinalizedVersion == "2");
        Assert.Equal(HttpStatusCode.OK, (await Orders()).Status);
    }

    /// <summary>
    /// The same package on a host whose <c>Elsa.Persistence.Schema</c> share declares major 0, as every Elsa share did
    /// before #2150: Nuplane's matcher does not take it, the package's own copy is the one the module binds, and nothing
    /// fails. The host starts, the module admits and its feature is mapped, but its gate never finalizes and the host's
    /// dormancy check never sees it, so the feature is refused as not observed. The share, not the copy, decides.
    /// </summary>
    [Fact]
    public async Task A_carried_copy_whose_share_declares_another_major_splits_the_module_from_the_host_and_its_feature_is_never_observed()
    {
        await SeedAsync(ConnectionString);

        await StartAsync(feed.CarryingDirectory, (ShareSetting("Elsa.Persistence.Schema", "MajorVersion"), "0"));
        await Task.Delay(TimeSpan.FromSeconds(2));

        var (status, body) = await Orders();
        Assert.Equal((HttpStatusCode.Conflict, "1"), (status, (await RecordAsync(ConnectionString)).FinalizedVersion));
        Assert.Contains("has not read the finalization record", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A share the host has no copy of refuses the package that carries it, and the host with it: Nuplane will not load
    /// the package's copy of an assembly its policy leaves to the host, so the host stops at its startup reconciliation
    /// and names the package, the assembly and the entry, rather than serving without the module. Here the entry is one
    /// for <c>Elsa.Persistence.EntityFramework</c>, which this host does not carry.
    /// </summary>
    [Fact]
    public async Task A_share_the_host_has_no_copy_of_refuses_the_package_that_carries_it_and_the_host_does_not_start()
    {
        await SeedAsync(ConnectionString);
        var added = $"Nuplane:Loading:SharedAssemblies:{RawShares().Length}";

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(
            ($"{added}:Name", "Elsa.Persistence.EntityFramework"),
            ($"{added}:MajorVersion", "4")));

        Assert.Contains("NuplaneStartupReconciliationException", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(
            "Package 'Elsa.Persistence.EntityFramework@4.0.0-dev' carries shared assembly 'Elsa.Persistence.EntityFramework' " +
            "(public key token: unsigned, major version: 4), which the shared-assembly policy leaves to the host, but the host " +
            "has no copy of it with that major version.",
            refusal.Message,
            StringComparison.Ordinal);
    }

    private Task StartAsync(params (string Key, string Value)[] settings) => StartAsync(feed.Directory, settings);

    private async Task StartAsync(string packages, params (string Key, string Value)[] settings) => _host = await FoundationHostProcess.StartAsync(
        Shells(ConnectionString),
        packages,
        new Dictionary<string, string>(settings.Select(setting => KeyValuePair.Create(setting.Key, setting.Value)))
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

    /// <summary>Every copy of <paramref name="share"/> Nuplane extracted with the fixture's package.</summary>
    private string[] Carried(string share) =>
    [
        .. Directory.EnumerateFiles(_host!.PackageInstallRoot, $"{share}.dll", SearchOption.AllDirectories)
            .Where(path => path.Contains(FoundationHostFeed.FixturePackage, StringComparison.OrdinalIgnoreCase))
    ];

    /// <summary>The host's <c>Nuplane:Loading:SharedAssemblies</c> entries as its <c>appsettings.json</c> lists them.</summary>
    private static IConfigurationSection[] RawShares() =>
    [
        .. new ConfigurationBuilder()
            .AddJsonFile(Path.Join(FoundationHostProcess.RepoRoot, "src", "apps", FoundationHost, "appsettings.json"))
            .Build()
            .GetSection("Nuplane:Loading:SharedAssemblies")
            .GetChildren()
    ];

    /// <summary>The setting that overrides <paramref name="property"/> of the host's share of <paramref name="name"/>.</summary>
    private static string ShareSetting(string name, string property) =>
        $"{RawShares().Single(entry => entry["Name"] == name).Path}:{property}";

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
