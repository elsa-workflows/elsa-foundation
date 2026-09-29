using System.Reflection;
using System.Runtime.Loader;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Readability;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// A host that composes cluster membership on its own container exactly as <c>Elsa.Foundation.Host</c> and
/// <c>Elsa.Workbench</c> do (<see cref="EfSchemaReadabilityServiceCollectionExtensions.AddEfSchemaReadability"/>), and
/// activates a real CShells shell whose features come from the feed-module fixture, loaded the way Nuplane loads a
/// package: into a load context of its own, carrying its own copy of every Elsa assembly the host does not share (#2143).
/// The host's shares are read from the host's own <c>appsettings.json</c> and matched by Nuplane's own matcher (#2150), so
/// removing one there, or declaring it at a major its assembly is not built with, is what these tests see.
/// </summary>
internal sealed class FeedLoadedModuleHost : IAsyncDisposable
{
    public const string FoundationHost = "Elsa.Foundation.Host";
    public const string Workbench = "Elsa.Workbench";
    private const string ShellName = "default";

    private readonly ServiceProvider _root;
    private readonly IShell _shell;
    private readonly IShellScope _scope;

    private FeedLoadedModuleHost(ServiceProvider root, IShell shell, PackageLoadContext package, Assembly module)
    {
        _root = root;
        _shell = shell;
        _scope = shell.BeginScope();
        Package = package;
        Module = module;
    }

    /// <summary>The load context the fixture was loaded into.</summary>
    public PackageLoadContext Package { get; }

    /// <summary>The fixture's assembly, as the package's load context loaded it.</summary>
    public Assembly Module { get; }

    /// <summary>A scope of the activated shell.</summary>
    public IServiceProvider Shell => _scope.ServiceProvider;

    /// <summary>The fixture's family, and its migrations-history name, restated by <see cref="FeedModuleDatabase"/>.</summary>
    public const string Family = FeedModuleDatabase.Family;

    public const string HistoryModule = FeedModuleDatabase.HistoryModule;

    /// <summary>The EF module, as its <c>[EfModule]</c> names it.</summary>
    public const string ModuleName = "FeedModuleFixture";

    /// <summary>The one migration the fixture's next release adds, and the table it creates, restated by <see cref="FeedModuleDatabase"/>.</summary>
    public const string NextReleaseMigration = FeedModuleDatabase.NextReleaseMigration;

    public const string TagsTable = FeedModuleDatabase.TagsTable;

    /// <summary>
    /// Starts the host and activates its shell. <paramref name="composeMembership"/> runs on the host container before the
    /// readability composition, as a host that selects a durable provider in configuration composes it.
    /// <paramref name="withheld"/> names shares to leave out, for the direction a missing share takes.
    /// </summary>
    public static async Task<FeedLoadedModuleHost> StartAsync(
        string host,
        string connectionString,
        Action<IServiceCollection>? composeMembership = null,
        params string[] withheld)
    {
        var package = new PackageLoadContext(SharedAssemblies(host).Without(withheld));
        var module = package.LoadFromAssemblyPath(FixturePath);
        foreach (var (name, expected) in new[]
                 {
                     ("Family", Family), ("HistoryModuleName", HistoryModule), ("Name", ModuleName),
                     ("NextReleaseMigration", NextReleaseMigration), ("TagsTable", TagsTable)
                 })
        {
            if (Constant(module, name) != expected)
                throw new InvalidOperationException($"The feed-module fixture's {name} is '{Constant(module, name)}', not the '{expected}' these tests seed.");
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            // The fixture ships no migrations: its database is created by the test, as a release before it did.
            [$"{EfMigrateOptions.SectionName}:{nameof(EfMigrateOptions.Policy)}"] = nameof(EfMigratePolicy.Validate),
            [$"{EfSchemaFinalizationOptions.SectionName}:{nameof(EfSchemaFinalizationOptions.EvaluationInterval)}"] = "00:00:00.200",
            [$"{EfSchemaFinalizationOptions.SectionName}:{nameof(EfSchemaFinalizationOptions.RefreshInterval)}"] = "00:00:00.100"
        }).Build());
        composeMembership?.Invoke(services);
        services.AddEfSchemaReadability();
        services.AddCShells(builder => builder
            .WithAssemblies(module)
            .AddShell(ShellName, shell => shell
                .WithFeature(Constant(module, "EntityFrameworkCoreFeature"), feature => feature.WithSetting("ConnectionString", connectionString))
                .WithFeature(Constant(module, "OrdersFeature"))));

        var root = services.BuildServiceProvider();
        try
        {
            var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName, CancellationToken.None);
            return new FeedLoadedModuleHost(root, shell, package, module);
        }
        catch
        {
            await root.DisposeAsync();
            package.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Calls the orders feature the way a request does: it asks the host's shared dormancy check before it accepts data
    /// only version 2 holds, and refuses while the feature is dormant.
    /// </summary>
    public async Task PlaceOrderAsync()
    {
        var orders = Shell.GetRequiredService(Module.GetType("Elsa.Cluster.Fixtures.FeedModule.FeedModuleOrders", throwOnError: true)!);
        await (Task)orders.GetType().GetMethod("PlaceAsync")!.Invoke(orders, [CancellationToken.None])!;
    }

    /// <summary>The requirements the fixture's orders feature declares, read as a feature catalog reads them.</summary>
    public IReadOnlyList<SchemaVersionRequirement> OrdersRequirements =>
        SchemaVersionRequirement.DeclaredBy(Module.GetType("Elsa.Cluster.Fixtures.FeedModule.FeedModuleOrdersFeature", throwOnError: true)!);

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        // Draining disposes the shell's container, which stops each gate's background evaluation and refresh.
        await (await _root.GetRequiredService<IShellRegistry>().DrainAsync(_shell)).WaitAsync();
        await _root.DisposeAsync();
        Package.Dispose();
        SqliteConnection.ClearAllPools();
    }

    /// <summary>The fixture's database on SQLite.</summary>
    private static FeedModuleDatabase Sqlite(string connectionString) => new((builder, cs) => builder.UseSqlite(cs), connectionString);

    public static Task SeedAsync(string connectionString, string? hold = null) => Sqlite(connectionString).SeedAsync(hold);

    public static Task ReleaseHoldAsync(string connectionString) => Sqlite(connectionString).ReleaseHoldAsync();

    /// <inheritdoc cref="FeedModuleDatabase.ApplyNextReleaseMigrationAsync"/>
    public static Task ApplyNextReleaseMigrationAsync(string connectionString) => Sqlite(connectionString).ApplyNextReleaseMigrationAsync();

    public static Task<SchemaFinalizationRecord> RecordAsync(string connectionString) => Sqlite(connectionString).RecordAsync();

    /// <summary>Deletes a SQLite database file and the journal, WAL and shared-memory files beside it.</summary>
    public static void DeleteDatabaseFiles(string file)
    {
        foreach (var path in new[] { file, file + "-journal", file + "-wal", file + "-shm" })
            File.Delete(path);
    }

    public static readonly string[] Chain = FeedModuleDatabase.Chain;

    /// <summary>The major of the fixture's own assembly, which no host carries, so a share for it names what a package's own copy is.</summary>
    public static int FixtureMajor => AssemblyName.GetAssemblyName(FixturePath).Version!.Major;

    private static string FixturePath => Path.Join(AppContext.BaseDirectory, "feed-module", "Elsa.Cluster.Fixtures.FeedModule.dll");

    private static string Constant(Assembly module, string name) =>
        (string)module.GetType("Elsa.Cluster.Fixtures.FeedModule.FeedModule", throwOnError: true)!.GetField(name)!.GetRawConstantValue()!;

    /// <summary>The host's <c>Nuplane:Loading:SharedAssemblies</c>, from its <c>appsettings.json</c>, as Nuplane binds and validates them.</summary>
    public static NuplaneSharedAssemblyPolicy SharedAssemblies(string host) =>
        NuplaneSharedAssemblyPolicy.FromHostAppSettings(Path.Join(FoundationHostProcess.RepoRoot, "src", "apps", host, "appsettings.json"));
}

/// <summary>
/// A load context of the kind Nuplane gives an EF module package, which declares itself host-integrated in its
/// <c>nuplane.json</c>, as the fixture's does: an assembly the host shares resolves to the host's copy; every other Elsa assembly
/// the package needs is a private copy of its own, loaded from the package's files. EF Core is the host's: both hosts carry
/// it, <c>Elsa.Foundation.Host</c> for its cluster membership since #2151. The package's assemblies are made visible to the
/// host's own context through its <c>Resolving</c> event, as Nuplane's host-integrated resolver does, so EF Core loaded by
/// the host finds the module's migrations assembly. Anything else, the framework and CShells, is the test process's.
/// </summary>
/// <remarks>
/// A share is decided as the real host decides it (#2150): by Nuplane's own matcher, over the entries Nuplane binds and
/// validates from the host's <c>appsettings.json</c>, on the name, public key token and major version the fixture's
/// reference carries. An entry that would not match on the real host, such as one declaring another major than the
/// assembly is built with, does not match here either, and the package loads its own copy.
/// </remarks>
internal sealed class PackageLoadContext : AssemblyLoadContext, IDisposable
{
    private static readonly string[] Directories = [Path.Join(AppContext.BaseDirectory, "feed-module"), AppContext.BaseDirectory];
    private readonly NuplaneSharedAssemblyPolicy _shared;

    public PackageLoadContext(NuplaneSharedAssemblyPolicy shared)
        : base($"feed-module-package-{Guid.NewGuid():N}", isCollectible: false)
    {
        _shared = shared;
        Default.Resolving += ResolveForHost;
    }

    public void Dispose() => Default.Resolving -= ResolveForHost;

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not { } name)
            return null;
        if (_shared.Shares(assemblyName))
            return Default.LoadFromAssemblyName(assemblyName);
        if (!name.StartsWith("Elsa.", StringComparison.Ordinal))
            return null;
        return Directories.Select(directory => Path.Join(directory, name + ".dll")).FirstOrDefault(File.Exists) is { } path
            ? LoadFromAssemblyPath(path)
            : null;
    }

    private Assembly? ResolveForHost(AssemblyLoadContext context, AssemblyName assemblyName) =>
        Assemblies.FirstOrDefault(assembly => AssemblyName.ReferenceMatchesDefinition(assemblyName, assembly.GetName()));
}
