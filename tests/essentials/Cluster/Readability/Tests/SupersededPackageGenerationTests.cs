using System.Reflection;
using System.Runtime.Loader;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Persistence.Schema;
using Microsoft.Extensions.DependencyInjection;
using Nuplane.Loading;

namespace Elsa.Cluster.Readability.Tests;

/// <summary>
/// Spec 183's FR-021, amended 2026-09-29: a package upgraded in place leaves its previous generation loaded in a Nuplane
/// load context for the life of the process, and that generation stops counting for the readability report once a newer
/// generation of the same assembly is the active one and no shell generation that has not been disposed still runs it.
/// Two real Nuplane load contexts hold the family's two generations: the previous one at version 1, reading 1 alone, and
/// the upgrade at version 2 with an upcaster from 1, reading both. Intersected, the host reads 1 and can never finalize 2.
/// </summary>
/// <remarks>
/// Both directions are pinned. Stopping too late looks like a healthy host that never finalizes; stopping too early
/// credits a version a generation still running cannot read, which looks like success until it reads a row it refuses.
/// </remarks>
public sealed class SupersededPackageGenerationTests : IAsyncDisposable
{
    private static readonly string[] Both = ["1", "2"];
    private static readonly string[] PreviousOnly = ["1"];

    private readonly string _family = $"Upgraded{Guid.NewGuid():N}";
    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("elsa-superseded-generations-");
    private readonly List<AssemblyLoadContext> _contexts = [];
    private readonly PackageCatalog _catalog = new();
    private readonly ServiceProvider _host;
    private readonly Assembly _previous;
    private readonly Assembly _current;

    public SupersededPackageGenerationTests()
    {
        var assemblyName = $"Upgraded.Module{Guid.NewGuid():N}";
        _previous = LoadIntoNuplaneContext(Image(assemblyName, new SyntheticFamily(_family, "Upgraded", "1")));
        _current = LoadIntoNuplaneContext(Image(assemblyName, new SyntheticFamily(_family, "Upgraded", "2", [new SyntheticUpcaster("Upgraded.OneToTwo", "1", "2")])));

        _host = Host(_catalog);
    }

    private ISupersededAssemblySource Generations => _host.GetRequiredService<ISupersededAssemblySource>();

    private IShellLifecycleSubscriber Lifecycle => (IShellLifecycleSubscriber)Generations;

    [Fact]
    public async Task Once_the_upgrade_is_the_active_package_the_previous_generation_stops_counting()
    {
        _catalog.Active = [_current];

        Assert.Equal(Both, await ReadableAsync());
    }

    /// <summary>
    /// Before the catalog lists the upgrade - its package is still loading, or it failed to load - nothing says the previous
    /// generation was replaced, so both count.
    /// </summary>
    [Fact]
    public async Task Until_the_catalog_lists_the_upgrade_both_generations_count()
    {
        _catalog.Active = [_previous];
        Assert.Equal(PreviousOnly, await ReadableAsync());

        _catalog.Active = [];
        Assert.Equal(PreviousOnly, await ReadableAsync());
    }

    /// <summary>
    /// A shell generation that still runs the previous generation - one draining after a reload, or one whose reload failed
    /// - keeps it counting, while the guard, which judges the generation about to be built, already sees only the upgrade.
    /// Its disposal publishes the host's report again, so a gate waiting on it evaluates at once.
    /// </summary>
    [Fact]
    public async Task While_a_shell_generation_still_runs_the_previous_generation_both_count_and_its_disposal_republishes()
    {
        _catalog.Active = [_current];
        var draining = Shell(_previous.GetType("Upgraded.OrderRow", throwOnError: true)!);
        await Lifecycle.OnStateChangedAsync(draining, ShellLifecycleState.Initializing, ShellLifecycleState.Active);
        var membership = _host.GetRequiredService<IClusterMembership>();
        await membership.PublishReportAsync();

        Assert.Equal(PreviousOnly, await ReadableAsync());
        Assert.Contains(_previous, await Generations.GetReplacedAsync());
        Assert.DoesNotContain(_previous, await Generations.GetRetiredAsync());

        await Lifecycle.OnStateChangedAsync(draining, ShellLifecycleState.Drained, ShellLifecycleState.Disposed);

        Assert.Equal(Both, await ReadableAsync());
        Assert.Equal(Both, Entry((await membership.ReadFleetAsync(FleetReadMode.Cached)).Members.Single()).ReadableVersions);
    }

    [Fact]
    public async Task A_shell_generation_that_runs_only_the_upgrade_holds_nothing_back()
    {
        _catalog.Active = [_current];
        await Lifecycle.OnStateChangedAsync(Shell(_current.GetType("Upgraded.OrderRow", throwOnError: true)!), ShellLifecycleState.Initializing, ShellLifecycleState.Active);

        Assert.Equal(Both, await ReadableAsync());
    }

    /// <summary>A shell generation whose features cannot be read may be running anything, so nothing it could run retires.</summary>
    [Fact]
    public async Task A_shell_generation_whose_features_cannot_be_read_keeps_every_replaced_generation_counting()
    {
        _catalog.Active = [_current];
        await Lifecycle.OnStateChangedAsync(new FakeShell(new ServiceCollection().BuildServiceProvider()), ShellLifecycleState.Initializing, ShellLifecycleState.Active);

        Assert.Equal(PreviousOnly, await ReadableAsync());
    }

    /// <summary>Only a context Nuplane created can hold a replaced generation: a same-named copy anywhere else keeps counting.</summary>
    [Fact]
    public async Task A_previous_generation_outside_a_nuplane_context_keeps_counting()
    {
        var elsewhere = new AssemblyLoadContext($"not-nuplane-{Guid.NewGuid():N}", isCollectible: true);
        _contexts.Add(elsewhere);
        elsewhere.LoadFromAssemblyPath(_previous.Location);
        _catalog.Active = [_current];

        Assert.Contains(_previous, await Generations.GetRetiredAsync());
        Assert.Equal(PreviousOnly, await ReadableAsync());
    }

    [Fact]
    public async Task A_host_without_nuplane_counts_every_generation()
    {
        await using var host = Host(catalog: null);

        Assert.Empty(await host.GetRequiredService<ISupersededAssemblySource>().GetRetiredAsync());
        Assert.Equal(PreviousOnly, await ReadableAsync(host));
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        _contexts.ForEach(context => context.Unload());
        try
        {
            _files.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A file an unloading context still maps is left for the temp directory's own cleanup.
        }
    }

    /// <summary>
    /// A host composed as Elsa.Foundation.Host composes it, with Nuplane's catalog when <paramref name="catalog"/> is given,
    /// and bound the way CShells binds it: by resolving its lifecycle subscribers from the host's container.
    /// </summary>
    private static ServiceProvider Host(PackageCatalog? catalog)
    {
        var services = new ServiceCollection().Configure<ClusterMembershipOptions>(options => options.HostId = $"superseded-{Guid.NewGuid():N}");
        if (catalog is not null)
            services.AddSingleton<IPackageAssemblyCatalog>(catalog);
        var host = services.AddEfSchemaReadability().BuildServiceProvider();
        _ = host.GetServices<IShellLifecycleSubscriber>().ToArray();
        return host;
    }

    private Task<IReadOnlyList<string>> ReadableAsync() => ReadableAsync(_host);

    private async Task<IReadOnlyList<string>> ReadableAsync(IServiceProvider host) =>
        Entry(await host.GetServices<IMemberReportSource<ReadabilitySection>>().Single().ReadAsync()).ReadableVersions;

    private ReadabilityEntry Entry(ReadabilitySection section) => section.Entries.Single(entry => entry.Family == _family);

    private ReadabilityEntry Entry(FleetMember member) => Entry(member.Report.Readability!);

    private static byte[] Image(string assemblyName, SyntheticFamily family) =>
        SyntheticSchemaFamilies.Image(assemblyName, ["Upgraded"], [new SyntheticColumn(family.Name, "Upgraded.OrderRow", "ContentJson")], family);

    /// <summary>Loads <paramref name="image"/> the way Nuplane loads a package: from its file, into a context Nuplane defines.</summary>
    private Assembly LoadIntoNuplaneContext(byte[] image)
    {
        var path = Path.Join(_files.CreateSubdirectory(Guid.NewGuid().ToString("N")).FullName, "Upgraded.Module.dll");
        File.WriteAllBytes(path, image);
        var context = new PackageAssemblyLoadContext(path, [], new SharedAssemblyPolicyMatcher());
        _contexts.Add(context);
        return context.LoadFromAssemblyPath(path);
    }

    private static FakeShell Shell(Type feature) =>
        new(new ServiceCollection()
            .AddSingleton<IReadOnlyCollection<ShellFeatureDescriptor>>([new ShellFeatureDescriptor("UpgradedOrders") { StartupType = feature }])
            .BuildServiceProvider());

    /// <summary>What Nuplane's catalog lists for the active package set: the assemblies of the one package these tests upgrade.</summary>
    private sealed class PackageCatalog : IPackageAssemblyCatalog
    {
        public IReadOnlyList<Assembly> Active { get; set; } = [];

        public Task<IReadOnlyList<PackageAssemblies>> GetPackagedAssembliesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PackageAssemblies>>(Active.Count == 0 ? [] : [new PackageAssemblies("Upgraded.Module", "0.0.0", Active, [])]);

        public Task<PackageAssemblies?> GetPackagedAssembliesAsync(string packageId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>A shell generation as a lifecycle subscriber sees it: a container that names the features it composed.</summary>
    private sealed class FakeShell(IServiceProvider services) : IShell
    {
        public ShellDescriptor Descriptor { get; } = ShellDescriptor.Create("default", 1);

        public ShellLifecycleState State => ShellLifecycleState.Active;

        public IServiceProvider ServiceProvider => services;

        public IDrainOperation? Drain => null;

        public IShellScope BeginScope() => throw new NotSupportedException();
    }
}
