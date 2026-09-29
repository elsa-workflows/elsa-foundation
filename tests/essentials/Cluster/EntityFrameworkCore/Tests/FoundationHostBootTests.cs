using System.Diagnostics;
using System.Net;
using System.Reflection;
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
public sealed class FoundationHostBootTests(FoundationHostFeed feed) : IClassFixture<FoundationHostFeed>, IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);
    private readonly string _file = Path.Join(Path.GetTempPath(), $"elsa-foundation-host-boot-{Guid.NewGuid():N}.db");
    private FoundationHostProcess? _host;

    private string ConnectionString => $"Data Source={_file};Pooling=False";

    public ValueTask DisposeAsync()
    {
        foreach (var file in new[] { _file, _file + "-journal", _file + "-wal", _file + "-shm" })
            File.Delete(file);
        return _host?.DisposeAsync() ?? ValueTask.CompletedTask;
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

/// <summary>
/// The feeds the host loads from: the fixture and <c>Elsa.Persistence.EntityFramework</c>, packed by the SDK from the build
/// this test assembly was built with, in <see cref="Directory"/>; and the packages they and the EF provider engine depend
/// on that the host does not carry (EF Core, the Sqlite engine and what they need), copied from the package cache this
/// project's restore filled, in <see cref="ClosureDirectory"/>.
/// </summary>
public sealed class FoundationHostFeed : IAsyncLifetime
{
    private static readonly string[] Projects =
    [
        Path.Join("tests", "essentials", "Cluster", "Fixtures", "FeedModule", "Elsa.Cluster.Fixtures.FeedModule.csproj"),
        Path.Join("src", "essentials", "Persistence", "EntityFramework", "Elsa.Persistence.EntityFramework.csproj")
    ];

    /// <summary>The EF persistence project's restore: every package it needs, which the packed package declares.</summary>
    private static readonly string PersistenceAssets = Path.Join(FoundationHostProcess.RepoRoot, "src", "essentials", "Persistence", "EntityFramework", "obj", "project.assets.json");

    /// <summary>This project's restore, which carries the Sqlite engine and the rest of what it needs.</summary>
    private static readonly string TestAssets = Path.Join(FoundationHostProcess.RepoRoot, "tests", "essentials", "Cluster", "EntityFrameworkCore", "Tests", "obj", "project.assets.json");

    private readonly DirectoryInfo _root = System.IO.Directory.CreateTempSubdirectory("elsa-foundation-host-feeds-");

    /// <summary>The packed packages, which the host takes as its own feed.</summary>
    public string Directory => Path.Join(_root.FullName, "packed");

    /// <summary>The packages those depend on, which the host only resolves from.</summary>
    public string ClosureDirectory => Path.Join(_root.FullName, "closure");

    public async Task InitializeAsync()
    {
        var configuration = typeof(FoundationHostFeed).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        foreach (var project in Projects)
            await DotnetAsync("pack", Path.Join(FoundationHostProcess.RepoRoot, project), "--no-build", "-c", configuration, "-p:IsPackable=true", "-o", Directory);

        System.IO.Directory.CreateDirectory(ClosureDirectory);
        // Both restores prune what ASP.NET's shared framework carries, which the host does not offer Nuplane, so the EF
        // persistence project's, a class library, is the one that names EF Core's own closure.
        foreach (var package in Packages(PersistenceAssets).Concat(Packages(TestAssets, "Microsoft.EntityFrameworkCore.Sqlite")).Distinct())
            File.Copy(package, Path.Join(ClosureDirectory, Path.GetFileName(package)), overwrite: true);
    }

    public Task DisposeAsync()
    {
        _root.Delete(recursive: true);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The <c>.nupkg</c> of each of <paramref name="roots"/> and of everything they depend on, as the restore of
    /// <paramref name="assetsFile"/> resolved them; of every package it resolved when no root is named.
    /// </summary>
    private static IEnumerable<string> Packages(string assetsFile, params string[] roots)
    {
        var assets = JsonNode.Parse(File.ReadAllText(assetsFile))!;
        var cache = assets["packageFolders"]!.AsObject().First().Key;
        var libraries = assets["libraries"]!.AsObject();
        var target = assets["targets"]!.AsObject().Single().Value!.AsObject();
        var byId = target.ToDictionary(entry => entry.Key[..entry.Key.IndexOf('/')], entry => entry.Key, StringComparer.OrdinalIgnoreCase);

        var pending = new Stack<string>(roots.Length == 0 ? byId.Keys : roots);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.TryPop(out var id))
        {
            if (!byId.TryGetValue(id, out var key) || !seen.Add(id) || libraries[key]!["type"]!.GetValue<string>() != "package")
                continue;

            var library = libraries[key]!;
            var file = library["files"]!.AsArray().Select(entry => entry!.GetValue<string>()).Single(name => name.EndsWith(".nupkg.sha512", StringComparison.Ordinal));
            yield return Path.Join(cache, library["path"]!.GetValue<string>(), file[..^".sha512".Length]);

            foreach (var dependency in target[key]!["dependencies"]?.AsObject().Select(entry => entry.Key) ?? [])
                pending.Push(dependency);
        }
    }

    private static async Task DotnetAsync(params string[] arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        })!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"dotnet {string.Join(' ', arguments)} exited {process.ExitCode}:{Environment.NewLine}{await output}{await error}");
    }
}
