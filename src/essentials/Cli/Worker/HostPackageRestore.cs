namespace Elsa.Cli.Worker;

/// <summary>What a restore did, in terms free of every Nuplane type.</summary>
internal enum RestoreVerdict
{
    /// <summary>A package set was already recorded, so nothing was composed, resolved or written.</summary>
    AlreadyRecorded,

    /// <summary>One cycle ran and every desired package is installed and recorded.</summary>
    Restored,

    /// <summary>At least one desired request names more than one version; nothing was resolved or written.</summary>
    Unpinned,

    /// <summary>
    /// At least one configured feed declares a credential reference that could not be resolved, so the feed
    /// is refused by name and nothing was restored.
    /// </summary>
    CredentialsRefused,

    /// <summary>Another process holds the store lock; nothing was read, resolved or written.</summary>
    StoreLocked,

    /// <summary>
    /// At least one package declares a capability the host has not resolvably selected, so the cycle refused
    /// it rather than installing a module without the engine it binds (spec 172 FR-005).
    /// </summary>
    CapabilityRefused,

    /// <summary>The cycle completed without installing everything it wanted.</summary>
    Degraded
}

/// <summary>Where one restore would write, decided by the caller rather than defaulted by Nuplane.</summary>
internal sealed record RestoreTarget(
    string HostDirectory,
    string Environment,
    string StateFile,
    bool HasDirectoryFeedModule);

/// <summary>
/// What one restore did. Deliberately free of every Nuplane type so <see cref="HostPackageRestore"/> can
/// turn it into a refusal without naming one.
/// </summary>
internal sealed record RestoreOutcome(
    RestoreVerdict Verdict,
    int PackageCount,
    string StateFile,
    string InstallRoot,
    IReadOnlyList<string> Offenders)
{
    public static RestoreOutcome AlreadyRecorded(int packages, string stateFile) =>
        new(RestoreVerdict.AlreadyRecorded, packages, stateFile, "", []);

    public static RestoreOutcome Restored(int packages, string stateFile, string installRoot) =>
        new(RestoreVerdict.Restored, packages, stateFile, installRoot, []);

    public static RestoreOutcome Unpinned(IReadOnlyList<string> requests, string stateFile, string installRoot) =>
        new(RestoreVerdict.Unpinned, 0, stateFile, installRoot, requests);

    public static RestoreOutcome CredentialsRefused(IReadOnlyList<string> feeds, string stateFile, string installRoot) =>
        new(RestoreVerdict.CredentialsRefused, 0, stateFile, installRoot, feeds);

    public static RestoreOutcome StoreLocked(string stateFile, string installRoot) =>
        new(RestoreVerdict.StoreLocked, 0, stateFile, installRoot, []);

    public static RestoreOutcome CapabilityRefused(IReadOnlyList<string> refusals, string stateFile, string installRoot) =>
        new(RestoreVerdict.CapabilityRefused, 0, stateFile, installRoot, refusals);

    public static RestoreOutcome Degraded(IReadOnlyList<string> failed, string stateFile, string installRoot) =>
        new(RestoreVerdict.Degraded, 0, stateFile, installRoot, failed);
}

/// <summary>
/// The opt-in <c>--restore</c> pass (ADR 0076 D10's exception, spec 171 FR-083 to FR-086): everything that
/// decides whether the tool may populate this host's package set, and every refusal that follows from what
/// the attempt reported.
/// </summary>
/// <remarks>
/// <para>
/// Free of every Nuplane type, for the same reason <see cref="NuplanePackageSet"/> is: the worker runs on
/// the host's deps file and carries no Nuplane of its own, so a host with none cannot even load the types a
/// Nuplane call site mentions. Deciding here — was the flag given, is a package set already recorded, does
/// this host carry Nuplane, does it carry the directory-feed module — means a host that cannot restore gets
/// a named refusal rather than a missing-assembly failure from the runtime.
/// </para>
/// <para>
/// Without the flag this type does nothing at all, and nothing else sets it: no command implies
/// <c>--restore</c>, so the tool's "never downloads" rule holds exactly as it did before.
/// </para>
/// </remarks>
internal static class HostPackageRestore
{
    /// <summary>The package a host carries when its feeds are directories rather than service indexes.</summary>
    private const string DirectoryFeedPackageId = "Nuplane.Sources.Directory";

    /// <summary>The package whose presence makes a host one whose modules arrive as packages at all.</summary>
    public const string NuplanePackageId = "Nuplane";

    /// <summary>
    /// Runs the restore, when <c>--restore</c> was given and this host is one that can be restored. Reports
    /// one line on stderr either way — what was restored, or that a set was already recorded — and refuses
    /// rather than scripting against a package set the attempt could not fully assemble.
    /// </summary>
    public static async Task RunAsync(WorkerRequest request, HostDepsFile deps, CancellationToken cancellationToken)
    {
        if (!request.Restore)
            return;

        var hostDirectory = request.HostDirectory!;
        var environment = request.Environment!;

        // A --packages root is an already-assembled set, so the two are refused as a usage error by the
        // front end (CliRefusal). Repeated here because the worker answers requests, not command lines, and
        // a request carrying both would otherwise restore into a host the run never reads from.
        if (request.PackageRoots.Count > 0)
        {
            throw WorkerRefusal.Usage(
                "restore-with-packages",
                "--restore and --packages cannot be combined: a --packages root is an already-assembled package set, " +
                "and nothing populates one. Give one or the other.");
        }

        if (deps.ForPackage(NuplanePackageId) is null)
        {
            throw WorkerRefusal.Resolution(
                "restore-unavailable",
                $"--restore populates a host's Nuplane package set, and this host pins no '{NuplanePackageId}': " +
                $"its dependency file '{Path.GetFileName(request.DepsFile!)}' lists none. " +
                "A host that carries every module in its own dependency file has no package set to populate.");
        }

        if (!HostAppSettings.Exist(hostDirectory, environment))
        {
            throw WorkerRefusal.Resolution(
                "restore-configuration-missing",
                $"--restore reads this host's own Nuplane configuration, and neither '{HostAppSettings.BaseFileName}' nor " +
                $"'{HostAppSettings.OverlayFileName(environment)}' is beside the host at '{hostDirectory}'. " +
                "There is nothing that says which feeds to restore from, and no feed is assumed.");
        }

        var target = new RestoreTarget(
            hostDirectory,
            environment,
            NuplaneInstallRoot.DefaultStateFile(hostDirectory),
            deps.ForPackage(DirectoryFeedPackageId) is not null);

        Report(await Guarded(target, cancellationToken));
    }

    /// <summary>
    /// Turns one outcome into either the single line an operator reads or the refusal that stops the run.
    /// Every branch that did not fully assemble the package set refuses: a partial set scripts a partial
    /// artifact, which is the failure that looks like success.
    /// </summary>
    private static void Report(RestoreOutcome outcome)
    {
        switch (outcome.Verdict)
        {
            case RestoreVerdict.AlreadyRecorded:
                Console.Error.WriteLine(
                    $"restore: '{outcome.StateFile}' already records {outcome.PackageCount} package(s); --restore did nothing.");
                return;

            case RestoreVerdict.Restored:
                Console.Error.WriteLine(
                    $"restore: {outcome.PackageCount} package(s) installed under '{outcome.InstallRoot}'; " +
                    $"active set recorded in '{outcome.StateFile}'.");
                return;

            case RestoreVerdict.Unpinned:
                throw WorkerRefusal.Usage(
                    "restore-unpinned",
                    "--restore only restores single-point version pins, so the artifact it leads to is the one a started " +
                    "host produces for those same pins. These desired requests name more than one version, and nothing " +
                    "was resolved, downloaded, installed or written:",
                    outcome.Offenders);

            case RestoreVerdict.CredentialsRefused:
                throw WorkerRefusal.Resolution(
                    "restore-feed-credentials",
                    "For these feeds the credential reference could not be resolved, so each was dropped before the " +
                    "first network call rather than contacted and rejected. This run is refused instead of restoring a " +
                    "partial set from the feeds that remain. A restore composes no host DI, so the only provider it has " +
                    "is Nuplane's built-in 'env': secrets://env/NAME reads this process's environment, and every other " +
                    "provider — secrets://elsa/... included — is unresolvable here whatever the started host can do " +
                    "with it. Feeds whose credential reference could not be resolved:",
                    outcome.Offenders);

            // The one degraded cycle whose cause is a decision rather than a fault: the module packages are
            // on the feed and the engine may well be too, and what is missing is the host saying which
            // engine it wants. Reported as its own refusal so the fix names a configuration key instead of
            // sending an operator to look for an unreachable feed.
            case RestoreVerdict.CapabilityRefused:
                throw WorkerRefusal.Resolution(
                    "restore-capability-unresolved",
                    $"These packages need a provider engine the host has not resolvably selected, so the restore into " +
                    $"'{outcome.InstallRoot}' installed neither them nor an engine for them, and nothing is scripted from " +
                    $"a partial set. Set '{HostCapabilitySelection.Key}' in this host's own " +
                    $"'{HostAppSettings.BaseFileName}' to one of the options below, or name an engine package as an " +
                    "explicit root in its package closure — an explicit root wins over the selection. Nuplane refused:",
                    outcome.Offenders);

            case RestoreVerdict.StoreLocked:
                throw WorkerRefusal.Resolution(
                    "restore-store-locked",
                    $"Another process holds the store lock beside '{outcome.StateFile}', which usually means this host is " +
                    "running. This tool never restores under a running host: nothing was read, resolved or written. " +
                    "Stop the host, or let it finish its own reconcile, and run this again.");

            // RestoreVerdict.Degraded, and anything a later Nuplane adds: a cycle that did not finish is
            // refused rather than scripted from, whether or not this build knows the name of the reason.
            default:
                throw WorkerRefusal.Resolution(
                    "restore-incomplete",
                    $"The restore into '{outcome.InstallRoot}' did not install everything this host's configuration asks " +
                    "for, and a partial package set would be scripted as if it were the whole one. Nothing is scripted " +
                    "from it. Packages that could not be installed:",
                    outcome.Offenders.Count > 0
                        ? outcome.Offenders
                        : ["The cycle reported itself degraded without naming a package; a feed was unreachable."]);
        }
    }

    /// <summary>
    /// Turns everything that can go wrong on the way into Nuplane into a named refusal, the way
    /// <see cref="NuplanePackageSet"/> does for the loader. Nuplane reports a failed restore on its result
    /// rather than throwing, so anything that does throw is a malformed request — a path nothing pins, a
    /// configuration that names no feed, options that fail Nuplane's own validation — and says so.
    /// </summary>
    private static async Task<RestoreOutcome> Guarded(RestoreTarget target, CancellationToken cancellationToken)
    {
        try
        {
            return await NuplaneRestoreRunner.RunAsync(target, cancellationToken);
        }
        catch (WorkerRefusal)
        {
            throw;
        }
        catch (Exception failure) when (failure is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException or MissingMethodException or MissingMemberException)
        {
            throw WorkerRefusal.Resolution(
                "restore-unavailable",
                $"--restore needs this host's own Nuplane to populate '{target.StateFile}', and this host does not provide " +
                $"all of it: {failure.Message} " +
                "A host restored from directory feeds also carries Nuplane.Sources.Directory, and one that reads an " +
                "appsettings.json carries the JSON configuration provider.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Interrupted, not refused: the run claims neither a restored host nor a failed one.
            throw;
        }
        catch (Exception failure) when (WorkerRunner.IsNonFatal(failure))
        {
            throw WorkerRefusal.Resolution(
                "restore-failed",
                $"This host's Nuplane configuration could not be restored from: {failure.GetType().Name}: {failure.Message}");
        }
    }
}
