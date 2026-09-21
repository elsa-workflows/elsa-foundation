using System.Runtime.Loader;

namespace Elsa.Cli.Worker;

/// <summary>
/// The active package set a host resolves its modules and its provider engine from, once Nuplane's own
/// loader has loaded it (FR-005, FR-006, ADR 0076 D1/D12).
/// </summary>
/// <remarks>
/// Deliberately free of every Nuplane type, which all live in <see cref="NuplaneLoader"/>. The worker runs
/// on the host's deps file and carries no Nuplane of its own, so a host with none cannot even load the
/// types a Nuplane call site mentions: keeping the decisions here — is there a package root, is anything
/// installed in it, is it unambiguous — means a host with no Nuplane gets a named refusal rather than a
/// missing-assembly failure from the runtime.
/// </remarks>
internal sealed class NuplanePackageSet
{
    private readonly IReadOnlyList<InstalledPackage> packages;
    private Dictionary<string, PackageFacts>? byAssembly;

    private NuplanePackageSet(IReadOnlyList<InstalledPackage> packages, IReadOnlyDictionary<string, string> failures)
    {
        this.packages = packages;
        Failures = failures;
    }

    /// <summary>The empty set, for a host that carries every module in its own deps file (FR-007).</summary>
    public static NuplanePackageSet None { get; } = new([], new Dictionary<string, string>());

    /// <summary>Every package the loader could not load, by package id, so a missing module can be explained rather than merely absent.</summary>
    public IReadOnlyDictionary<string, string> Failures { get; }

    /// <summary>How many packages this set resolved, for the refusals that must say where the tool looked.</summary>
    public int Count => packages.Count;

    /// <summary>The package facts for an assembly loaded out of this set, or <c>null</c> when it came from somewhere else.</summary>
    public PackageFacts? ForAssembly(string assemblyName)
    {
        byAssembly ??= MapAssemblies();
        return byAssembly.GetValueOrDefault(assemblyName);
    }

    /// <summary>The package facts for a package id in this set, or <c>null</c> when the set does not carry it.</summary>
    public PackageFacts? ForPackage(string packageId) =>
        packages.FirstOrDefault(package => string.Equals(package.Id, packageId, StringComparison.OrdinalIgnoreCase)) is { } found
            ? new(found.Id, found.Version, PackageSource.ResolvedNupkg)
            : null;

    /// <summary>
    /// Loads the one package set this run resolves against. At most one load happens per process, because a
    /// host-integrated load is irreversible for the process lifetime and holds a process-wide lock
    /// (FR-073) — which is also why a run that would need two different kinds of load is refused rather
    /// than silently loading one of them.
    /// </summary>
    public static async Task<NuplanePackageSet> LoadAsync(IReadOnlyList<string> packageRoots, string hostDirectory, CancellationToken cancellationToken)
    {
        if (packageRoots.Count == 0)
        {
            var hostState = NuplaneInstallRoot.DefaultStateFile(hostDirectory);
            return File.Exists(hostState) ? await FromStateAsync(hostState, cancellationToken) : None;
        }

        var stateFiles = packageRoots
            .Select(root => Path.Join(root, NuplaneInstallRoot.StateFileName))
            .Where(File.Exists)
            .ToArray();
        var probeRoots = packageRoots
            .Where(root => !File.Exists(Path.Join(root, NuplaneInstallRoot.StateFileName)))
            .ToArray();

        if (stateFiles.Length == 1 && probeRoots.Length == 0)
            return await FromStateAsync(stateFiles[0], cancellationToken);

        if (stateFiles.Length > 0)
        {
            throw WorkerRefusal.Resolution(
                "packages-roots-inconsistent",
                $"One --packages root records its active set in a {NuplaneInstallRoot.StateFileName} and another does not. " +
                "A run loads exactly one package set, and the two are resolved differently, so neither is chosen for you.",
                [.. stateFiles.Select(file => $"'{Path.GetDirectoryName(file)}' has a {NuplaneInstallRoot.StateFileName}."),
                 .. probeRoots.Select(root => $"'{root}' has none.")]);
        }

        return await FromProbeAsync(probeRoots, cancellationToken);
    }

    /// <summary>
    /// The state file is the only source that records which packages a host loads together, so it is read
    /// through Nuplane's own reader and loaded through the entry point that keeps that grouping (FR-005).
    /// </summary>
    private static async Task<NuplanePackageSet> FromStateAsync(string stateFile, CancellationToken cancellationToken)
    {
        var loaded = await Guarded(() => NuplaneLoader.FromStateAsync(stateFile, cancellationToken), stateFile);
        if (loaded.Packages.Count == 0)
        {
            throw WorkerRefusal.Resolution(
                "packages-empty",
                $"'{stateFile}' records no active package. Nothing is installed there, and no command downloads packages to populate it.");
        }

        return new(loaded.Packages, loaded.Failures);
    }

    /// <summary>
    /// A root with no state file has no activation records to group by, so the set is assembled from the
    /// completion markers on disk and handed to the loader as one graph (FR-006). Everything that can be
    /// decided without Nuplane is decided here, before the loader is ever reached.
    /// </summary>
    private static async Task<NuplanePackageSet> FromProbeAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken)
    {
        var installed = roots.SelectMany(NuplaneInstallRoot.Probe).ToArray();
        if (installed.Length == 0)
        {
            throw WorkerRefusal.Resolution(
                "packages-empty",
                $"No package is installed under {string.Join(", ", roots.Select(root => $"'{root}'"))}: " +
                $"no directory there carries a {NuplaneInstallRoot.ReadyMarker} completion marker. " +
                "No command downloads packages to populate it.");
        }

        var duplicates = installed
            .GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"'{group.Key}' is installed under more than one --packages root.")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (duplicates.Length > 0)
            throw WorkerRefusal.Resolution("packages-ambiguous", "More than one --packages root installs the same package.", duplicates);

        var failures = await Guarded(
            () => NuplaneLoader.FromPackagesAsync(installed, cancellationToken),
            string.Join(", ", roots));
        return new(installed, failures.Failures);
    }

    /// <summary>
    /// Turns everything that can go wrong on the way into Nuplane into a named refusal. The failure worth
    /// spelling out is a host that carries no Nuplane at all: the worker's Nuplane call sites resolve out of
    /// the host's own closure, so against such a host they cannot even be compiled, and the runtime's
    /// missing-assembly error would name an assembly the operator never asked for.
    /// </summary>
    private static async Task<NuplaneLoadResult> Guarded(Func<Task<NuplaneLoadResult>> load, string source)
    {
        try
        {
            return await load();
        }
        catch (WorkerRefusal)
        {
            throw;
        }
        catch (Exception failure) when (failure is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException or MissingMethodException or MissingMemberException)
        {
            throw WorkerRefusal.Resolution(
                "packages-loader-unavailable",
                $"The package set at '{source}' needs this host's own Nuplane loader to resolve, and this host does not provide one: {failure.Message} " +
                "A host that carries every module in its own dependency file needs no --packages root at all.");
        }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException or IOException or System.Text.Json.JsonException)
        {
            throw WorkerRefusal.Resolution("packages-load-failed", $"The package set at '{source}' could not be loaded: {failure.Message}");
        }
    }

    /// <summary>
    /// Maps loaded assemblies back to the package they came from by install path. The loader reports which
    /// packages loaded, but the manifest needs the reverse mapping — this assembly's package id and version
    /// — and a path prefix is the only thing that states it for a package graph.
    /// </summary>
    private Dictionary<string, PackageFacts> MapAssemblies()
    {
        var map = new Dictionary<string, PackageFacts>(StringComparer.OrdinalIgnoreCase);
        var roots = packages
            .Select(package => (package, prefix: Path.TrimEndingDirectorySeparator(Path.GetFullPath(package.InstallPath)) + Path.DirectorySeparatorChar))
            .ToArray();

        foreach (var assembly in AssemblyLoadContext.All.SelectMany(context => context.Assemblies).Distinct())
        {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location) || assembly.GetName().Name is not { } name)
                continue;
            var location = Path.GetFullPath(assembly.Location);
            foreach (var (package, prefix) in roots)
                if (location.StartsWith(prefix, StringComparison.Ordinal))
                    map[name] = new(package.Id, package.Version, PackageSource.ResolvedNupkg);
        }

        return map;
    }
}

/// <summary>What one load resolved: the packages it covers, and why any of them could not be loaded.</summary>
internal sealed record NuplaneLoadResult(IReadOnlyList<InstalledPackage> Packages, IReadOnlyDictionary<string, string> Failures);
