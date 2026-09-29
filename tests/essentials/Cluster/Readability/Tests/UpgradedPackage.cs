using System.Reflection;
using System.Runtime.Loader;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Nuplane.Loading;

namespace Elsa.Cluster.Readability.Tests;

/// <summary>
/// One package upgraded in place, loaded where Nuplane loads a package: two generations of one assembly, each in a Nuplane
/// load context of its own, which is never unloaded while the host runs. The previous generation declares its family at
/// version 1 and reads 1 alone; the upgrade is at version 2 with an upcaster from 1 and reads both. Intersected, the host
/// reads 1 and can never finalize 2. Both carry the same CShells feature, so a shell composes whichever generation its
/// feature catalog names.
/// </summary>
internal sealed class UpgradedPackage : IDisposable
{
    /// <summary>What a host that can still run the previous generation reads, and one that cannot.</summary>
    public static readonly string[] PreviousOnly = ["1"], Both = ["1", "2"];

    /// <summary>The name CShells gives the feature both generations carry.</summary>
    public const string Feature = "UpgradedOrders";

    private const string FeatureType = "Upgraded.UpgradedOrdersFeature";

    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("elsa-superseded-generations-");
    private readonly List<AssemblyLoadContext> _contexts = [];

    public UpgradedPackage()
    {
        var assemblyName = $"Upgraded.Module{Guid.NewGuid():N}";
        Previous = Load(Image(assemblyName, new SyntheticFamily(Family, "Upgraded", "1")));
        Current = Load(Image(assemblyName, new SyntheticFamily(Family, "Upgraded", "2", [new SyntheticUpcaster("Upgraded.OneToTwo", "1", "2")])));
    }

    public string Family { get; } = $"Upgraded{Guid.NewGuid():N}";

    public Assembly Previous { get; }

    public Assembly Current { get; }

    /// <summary>What Nuplane's catalog lists for the active package set, which a test moves from one generation to the other.</summary>
    public PackageCatalog Catalog { get; } = new();

    public static Type FeatureOf(Assembly generation) => generation.GetType(FeatureType, throwOnError: true)!;

    /// <summary>
    /// Loads <paramref name="image"/> the way Nuplane loads a package, from its file: into a Nuplane load context of its
    /// own, or into <paramref name="beside"/>'s, as a graph context holds every package of its graph.
    /// It is named <paramref name="fileName"/>, which a copy of an assembly the host has must carry as the host's does.
    /// </summary>
    public Assembly Load(byte[] image, string fileName = "Upgraded.Package.dll", Assembly? beside = null)
    {
        var path = Path.Join(_files.CreateSubdirectory(Guid.NewGuid().ToString("N")).FullName, fileName);
        File.WriteAllBytes(path, image);
        if (beside is not null)
            return AssemblyLoadContext.GetLoadContext(beside)!.LoadFromAssemblyPath(path);

        var context = new PackageAssemblyLoadContext(path, [], new SharedAssemblyPolicyMatcher());
        _contexts.Add(context);
        return context.LoadFromAssemblyPath(path);
    }

    /// <summary>What <paramref name="host"/>'s readability report reads for this package's family.</summary>
    public async Task<IReadOnlyList<string>> ReadableAsync(IServiceProvider host) =>
        Entry(await host.GetServices<IMemberReportSource<ReadabilitySection>>().Single().ReadAsync()).ReadableVersions;

    /// <summary>What <paramref name="host"/> last published for this package's family, which a gate reads.</summary>
    public async Task<IReadOnlyList<string>> PublishedAsync(IServiceProvider host) =>
        Entry((await host.GetRequiredService<IClusterMembership>().ReadFleetAsync(FleetReadMode.Cached)).Members.Single().Report.Readability!).ReadableVersions;

    public void Dispose()
    {
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

    private ReadabilityEntry Entry(ReadabilitySection section) => section.Entries.Single(entry => entry.Family == Family);

    private static byte[] Image(string assemblyName, SyntheticFamily family) =>
        SyntheticSchemaFamilies.Image(assemblyName, ["Upgraded"], [new SyntheticColumn(family.Name, "Upgraded.OrderRow", "ContentJson")], [new SyntheticFeature(FeatureType)], family);

    /// <summary>What Nuplane's catalog lists for the active package set: the assemblies of the one package these tests upgrade.</summary>
    internal sealed class PackageCatalog : IPackageAssemblyCatalog
    {
        public IReadOnlyList<Assembly> Active { get; set; } = [];

        public Task<IReadOnlyList<PackageAssemblies>> GetPackagedAssembliesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PackageAssemblies>>(Active.Count == 0 ? [] : [new PackageAssemblies("Upgraded.Module", "0.0.0", Active, [])]);

        public Task<PackageAssemblies?> GetPackagedAssembliesAsync(string packageId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
