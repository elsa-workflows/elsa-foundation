using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nuplane.Admin;
using Nuplane.Builder;
using Nuplane.Feeds.Configuration;

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
        var refusal = "";
        for (var attempt = 1; attempt <= SettleAttempts; attempt++)
        {
            preview = await maintenance.PreviewEnrollmentAsync(PackageStoreEnrollment.RootLabel, cancellationToken);
            if (preview.Membership == "Complete")
            {
                logger.LogInformation("The Studio package store at {Root} is enrolled (epoch {Epoch}).", root, preview.EnrollmentEpoch);
                return;
            }

            if (preview.Membership == "Unenrolled" && attempt == 1)
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

                refusal = $" Enrollment answered {enrolled.Status ?? "unknown"}: {string.Join("; ", enrolled.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"))}.";
            }

            await Task.Delay(SettleDelay, cancellationToken);
        }

        var remedy = preview.Membership == "Incomplete"
            ? "An interrupted enrollment needs Nuplane's enrollment recovery while every process that uses the store is stopped"
            // A filesystem without the identity and naming operations Nuplane needs is refused before any membership can be
            // read. Nuplane 0.0.13 qualifies ext4 only on Linux and refuses a store inside a Linux container.
            : "Nuplane coordinates a package store only on a filesystem its native admission supports, such as ext4 on a Linux " +
              "machine, and refuses it inside a Linux container. Run the app on a supported filesystem";
        throw new StudioHostingException(
            $"The Studio package store at '{root}' is not enrolled (membership: {preview.Membership ?? "unknown"}).{refusal} " +
            $"{string.Join("; ", preview.Diagnostics)} {remedy}, or set {PackageStoreEnrollment.SwitchKey} to false.");
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
