using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nuplane.Admin;
using Nuplane.Builder;
using Nuplane.Feeds.Configuration;
using Nuplane.Store.State;

namespace Elsa.Studio.Hosting.Distribution;

/// <summary>
/// The shipped apps' package-store opt-in (#5778): with <see cref="SwitchKey"/> on, the app's Nuplane store is the coordinated
/// immutable extracted-store profile with this app as its only client, and a fresh store is enrolled before any Nuplane service
/// starts. Installed add-ons' browser assets are then served through Nuplane's admitted reader.
/// </summary>
internal static class PackageStoreEnrollment
{
    /// <summary>The switch, beside the other <c>Studio:Hosting:Nuplane</c> options. Off unless configured.</summary>
    public const string SwitchKey = "Studio:Hosting:Nuplane:EnrollPackageStore";

    /// <summary>Nuplane's label for the root its feed options resolve (<c>PackageInstallRoot</c>).</summary>
    public const string RootLabel = "default";

    /// <summary>The roster's one member: this app.</summary>
    public const string MemberId = "studio";

    public static bool IsOn(IConfiguration configuration) => configuration.GetValue<bool>(SwitchKey);

    /// <summary>
    /// Declares the complete one-member roster over the app's state file: the configured one, with Nuplane's precedence of
    /// <c>Nuplane:StoreRegistry:StateFilePath</c> over <c>Nuplane:Setup:StateFilePath</c>, otherwise Nuplane's default
    /// <c>.nuplane/store-state.json</c>, resolved against the content root.
    /// </summary>
    public static void Compose(NuplaneBuilder nuplane, IConfiguration configuration, string contentRoot)
    {
        var configured = configuration["Nuplane:StoreRegistry:StateFilePath"] ?? configuration["Nuplane:Setup:StateFilePath"];
        var state = Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? Path.Combine(".nuplane", "store-state.json") : configured, contentRoot);
        nuplane.WithStateFile(state).UseCoordinatedPackageStore([new(RootLabel, MemberId, state)], allClientsParticipate: true);
    }
}

/// <summary>
/// Enrolls a fresh store on first start and reuses the enrollment on every later start. It runs in
/// <see cref="IHostedLifecycleService.StartingAsync"/>, so no reconciliation, recovery or load has touched the store yet: the
/// quiescent cutover Nuplane requires. A store that holds files but is not enrolled is refused; enrolling it is the operator's
/// cutover, because other processes may still use it.
/// </summary>
internal sealed class PackageStoreEnrollmentStartup(
    INuplanePackageStoreMaintenanceOperations maintenance,
    IOptions<FeedResolutionOptions> feeds,
    IOptions<StoreRegistryOptions> stores,
    ILogger<PackageStoreEnrollmentStartup> logger) : IHostedLifecycleService
{
    // A second process that shares the content root can lose the enrollment race. Nuplane's root and member locks never wait,
    // so it is refused while the first finishes; it then reads the published enrollment.
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(250);
    private const int SettleAttempts = 20;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var root = feeds.Value.PackageInstallRoot
            ?? throw new StudioHostingException("The Studio package store has no install root to enroll.");
        // Nuplane observes an existing directory only; a first start has none yet.
        Directory.CreateDirectory(root);

        PackageStoreEnrollmentPreviewResult preview = null!;
        PackageStoreEnrollmentResult? refused = null;
        for (var attempt = 1; attempt <= SettleAttempts; attempt++)
        {
            preview = await maintenance.PreviewEnrollmentAsync(PackageStoreEnrollment.RootLabel, cancellationToken);
            if (preview.Membership == "Complete")
            {
                logger.LogInformation("The Studio package store at {Root} is enrolled (epoch {Epoch}).", root, preview.EnrollmentEpoch);
                return;
            }

            if (preview.Membership == "Unenrolled" && refused is null)
            {
                if (HoldsFiles(root))
                    throw new StudioHostingException(
                        $"The Studio package store at '{root}' holds files but is not enrolled, and {PackageStoreEnrollment.SwitchKey} " +
                        "enrolls only a fresh, empty store. Stop every process that uses the store and enroll it with Nuplane's " +
                        "package-store enrollment (see the Elsa.Studio.Hosting.Nuplane README), or empty that directory, or set " +
                        $"{PackageStoreEnrollment.SwitchKey} to false to run without serving installed add-ons' browser assets.");

                var enrolled = await maintenance.EnrollAsync(new(PackageStoreEnrollment.RootLabel, preview.RootIdentity!, 1, true,
                    [PackageStoreEnrollment.MemberId], []), cancellationToken);
                if (enrolled.Status == "Complete")
                {
                    logger.LogInformation("Enrolled the fresh Studio package store at {Root}.", root);
                    return;
                }

                refused = enrolled;
            }

            await Task.Delay(SettleDelay, cancellationToken);
        }

        throw new StudioHostingException(Diagnose(root, preview, refused));
    }

    /// <summary>What stopped startup once the store settled in a state other than Complete, and what the operator can do.</summary>
    private string Diagnose(string root, PackageStoreEnrollmentPreviewResult preview, PackageStoreEnrollmentResult? refused)
    {
        var observed = string.Join("; ", preview.Diagnostics);
        var off = $"Or set {PackageStoreEnrollment.SwitchKey} to false to run without serving installed add-ons' browser assets.";
        if (preview.Membership == "Incomplete")
        {
            // Nuplane's RecoverEnrollmentAsync resumes an interrupted enrollment, but only under an operator's assertion that
            // every client is stopped, which this process cannot make for others. A store with no installs loses nothing.
            var state = stores.Value.StateFilePath;
            return HoldsFiles(root)
                ? $"The Studio package store at '{root}' has an interrupted enrollment and holds installed packages. {observed} " +
                  "Stop every process that uses the store and resume the enrollment with Nuplane.Admin's RecoverEnrollmentAsync " +
                  $"(INuplanePackageStoreMaintenanceOperations). {off}"
                : $"The Studio package store at '{root}' has an interrupted enrollment and holds no installed packages. {observed} " +
                  $"Stop the app, delete '{root}' and the state file '{state}' (by default the whole .nuplane folder under the " +
                  $"content root), and start it again to enroll a fresh store. {off}";
        }

        if (refused is not null)
            return $"Nuplane refused to enroll the fresh Studio package store at '{root}': {refused.Status ?? "unknown"}, " +
                   $"{string.Join("; ", refused.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"))}. {observed} {off}";

        // An unreadable membership is either transient, another process holding the store's non-waiting locks for longer than
        // this start waited, or lasting, a filesystem without the identity and naming operations Nuplane needs.
        return $"Nuplane could not read the membership of the Studio package store at '{root}' within {(SettleDelay * SettleAttempts).TotalSeconds:0} seconds. " +
               $"{observed} If another process is using the store, start this one again once that process is idle. If the " +
               "diagnostic names a filesystem operation, the store is on a filesystem Nuplane's native admission does not " +
               $"support; Nuplane 0.0.13 qualifies ext4 on Linux. {off}";
    }

    /// <summary>
    /// Whether the store holds any file outside Nuplane's control directory. Directories alone hold no install. The control
    /// directory holds only lock files until an enrollment publishes its ledger, so a process that loses the enrollment race to
    /// another one, or follows an enrollment that stopped before it published, still sees a fresh store.
    /// </summary>
    private static bool HoldsFiles(string root) => Directory.EnumerateFileSystemEntries(root)
        .Where(entry => Path.GetFileName(entry) != ControlDirectoryName)
        .Any(entry => File.Exists(entry) || Directory.EnumerateFiles(entry, "*", SearchOption.AllDirectories).Any());

    // Nuplane's per-root control directory: root.lock, member locks and, once enrolled, membership.json.
    private const string ControlDirectoryName = ".nuplane-store";

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
