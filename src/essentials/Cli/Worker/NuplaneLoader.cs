using Nuplane;
using Nuplane.Abstractions;
using Nuplane.Loading;
using System.Runtime.CompilerServices;

namespace Elsa.Cli.Worker;

/// <summary>
/// Every Nuplane call the worker makes to <em>read and load</em> a host's package set.
/// </summary>
/// <remarks>
/// <para>
/// The worker compiles against Nuplane and ships none of it: the assemblies are excluded from its own
/// output and resolve out of the host's deps file, so a Nuplane host loads exactly the copy it runs itself
/// (ADR 0076 D1) and a host with no Nuplane never loads one at all. That only holds while no Nuplane type
/// is mentioned outside the files that are reached after a caller has decided a Nuplane host is present,
/// because the runtime resolves a method's types when it compiles that method — which is why the caller
/// decides whether there is a package set to load before calling in here. Three files carry that licence:
/// this one, <see cref="NuplaneRestoreRunner"/>, and <see cref="NuplaneDirectoryFeeds"/>.
/// </para>
/// <para>
/// Nothing on this path reconciles, downloads or writes (ADR 0076 D10). One reconcile pass writes the
/// host's <c>store-state.json</c> unconditionally, the store lock file beside it, and each acquired
/// package's extracted contents plus a <c>.tmp</c> staging directory under the install root; it deletes no
/// installed package — cleanup during a cycle records decisions and removes nothing, and the only deletes
/// are its own staging directory and an extraction that never completed, which carries no
/// <c>.nuplane-ready</c> marker. A tool sharing a host's directories still does none of that unless the
/// operator asks for it with <c>--restore</c>, which is <see cref="HostPackageRestore"/>'s job and no other
/// command's.
/// </para>
/// </remarks>
internal static class NuplaneLoader
{
    /// <summary>A racing host write can leave the state file locked, empty or torn; both failures are documented as transient.</summary>
    private const int StateReadAttempts = 5;

    private static readonly TimeSpan StateReadDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The host's active package set, read through Nuplane's own reader and nothing else (FR-072). A state
    /// file that is not there is an empty set, which is how the reader itself reports one.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task<IReadOnlyList<ActivePackage>> ReadActiveAsync(string stateFile, CancellationToken cancellationToken) =>
        ReadStateAsync(stateFile, cancellationToken);

    /// <summary>
    /// Reads the host's active set through Nuplane's own reader and loads it through the entry point that
    /// groups packages into graphs exactly as the host that wrote the state does (FR-005, FR-072, FR-073).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<NuplaneLoadResult> FromStateAsync(string stateFile, CancellationToken cancellationToken)
    {
        var active = await ReadStateAsync(stateFile, cancellationToken);
        if (active.Count == 0)
            return new([], new Dictionary<string, string>());

        // No TargetFrameworkOverride: a worker launched on the host's own runtimeconfig already runs the
        // host's target framework, so overriding it would select assets the host itself would not.
        var result = await NuplaneHostIntegratedLoader.LoadFromStateAsync(stateFile, options: null, cancellationToken);
        return new([.. active.Select(Describe)], result.FailedByPackageId);
    }

    /// <summary>
    /// Loads an already-assembled set — a <c>--packages</c> root with no state file — as one graph, which
    /// groups by graph generation identity rather than by the state's activation records (FR-006).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<NuplaneLoadResult> FromPackagesAsync(IReadOnlyList<InstalledPackage> installed, CancellationToken cancellationToken)
    {
        // One graph identity for the whole set: every package a caller pointed at is meant to resolve every
        // other one, which is what a single graph gives. The identity is constant rather than generated, so
        // two runs over the same directory load the same way.
        const string graph = "dotnet-elsa-packages";
        var roots = installed.Select(package => package.Id).Order(StringComparer.Ordinal).ToArray();
        var active = installed
            .Select(package => new ActivePackage(
                PackageId: package.Id,
                Version: package.Version,
                FeedName: package.FeedName,
                SourceName: "--packages",
                InstallPath: package.InstallPath,
                ActivatedAtUtc: package.InstalledAtUtc,
                ActivationCorrelationId: graph,
                GraphId: graph,
                GraphGenerationId: graph,
                PackageRole: ActivePackageRole.Root,
                RootPackageIds: roots,
                DependencyOfPackageIds: [],
                Discoverable: true))
            .ToArray();

        var result = await NuplaneHostIntegratedLoader.LoadActivePackagesAsync(active, options: null, cancellationToken);
        return new(installed, result.FailedByPackageId);
    }

    private static async Task<IReadOnlyList<ActivePackage>> ReadStateAsync(string stateFile, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await NuplaneStore.ReadActivePackagesAsync(stateFile, cancellationToken);
            }
            // A host writes its state file in place, so a reader can catch it locked, empty or torn. Both
            // failures are transient by construction; only a persistent one is a resolution failure.
            catch (Exception failure) when (failure is IOException or System.Text.Json.JsonException && attempt < StateReadAttempts)
            {
                await Task.Delay(StateReadDelay, cancellationToken);
            }
            catch (Exception failure) when (failure is IOException or System.Text.Json.JsonException)
            {
                throw WorkerRefusal.Resolution(
                    "packages-state-unreadable",
                    $"'{stateFile}' could not be read after {StateReadAttempts} attempts: {failure.Message}");
            }
        }
    }

    private static InstalledPackage Describe(ActivePackage package) =>
        new(package.PackageId, package.Version, package.InstallPath, package.FeedName ?? "", package.ActivatedAtUtc);
}
