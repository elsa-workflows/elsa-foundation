using Nuplane;
using System.Runtime.CompilerServices;

namespace Elsa.Cli.Worker;

/// <summary>
/// Every Nuplane call <c>--restore</c> makes: read what the host already records, ask what its
/// configuration wants, and run exactly one reconcile through Nuplane's own host-free entry point.
/// </summary>
/// <remarks>
/// <para>
/// Same isolation rule as <see cref="NuplaneLoader"/>: the runtime resolves a method's types when it
/// compiles that method, so the one entry point here is <see cref="MethodImplOptions.NoInlining"/> and is
/// reached only after <see cref="HostPackageRestore"/> has decided — without naming a Nuplane type — that
/// this host carries Nuplane at all. The private helpers below need no such attribute: nothing outside this
/// file can reach them, and the only method they can be folded into already names Nuplane types.
/// </para>
/// <para>
/// This is the one place in the tool that writes under the host's directories, and only when the operator
/// asked (ADR 0076 D10's opt-in exception). What one pass writes is bounded by Nuplane:
/// <c>store-state.json</c>, the store lock file beside it, and extracted packages plus a <c>.tmp</c>
/// staging directory under the install root. It deletes no installed package.
/// </para>
/// <para>
/// No <c>LoggerFactory</c> is supplied, so the composition emits no logs. That is deliberate rather than
/// incidental: a feed's configured credential reference is a value read out of the host's
/// <c>appsettings.json</c>, and a log sink the tool does not control is one more place it could surface.
/// Everything an operator needs is reported through the result instead.
/// </para>
/// </remarks>
internal static class NuplaneRestoreRunner
{
    /// <summary>
    /// The prefix on every stage Nuplane records for a capability it could not resolve:
    /// <c>capability-unselected</c>, <c>capability-unknown-option</c>, <c>capability-unpinned</c>,
    /// <c>capability-conflict</c>, <c>capability-unresolved</c>, and whatever a later Nuplane adds beside
    /// them. Matched by prefix on purpose — a stage this build has not heard of is still a capability
    /// refusal, and reporting it as an unreachable feed would be the wrong answer told confidently.
    /// </summary>
    private const string CapabilityStagePrefix = "capability-";

    /// <summary>
    /// Restores the host's package set, or reports why it did not. Failures Nuplane reports rather than
    /// throws — a degraded cycle, a failed package, a feed refused for its credentials, a store another
    /// process holds — all come back as a <see cref="RestoreOutcome"/> for the caller to refuse on.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<RestoreOutcome> RunAsync(RestoreTarget target, CancellationToken cancellationToken)
    {
        // Asked first, before anything is composed: a host that already records a package set is one
        // --restore leaves alone, and asking costs a single read of a file this tool reads anyway.
        var recorded = await NuplaneLoader.ReadActiveAsync(target.StateFile, cancellationToken);
        if (recorded.Count > 0)
            return RestoreOutcome.AlreadyRecorded(recorded.Count, target.StateFile);

        var configuration = HostAppSettings.Read(target.HostDirectory, target.Environment);
        var options = new NuplaneRestoreOptions
        {
            // Every Nuplane path resolves under the host rather than under this tool, and this one option is
            // what does it: the install root at BasePath/.nuplane/packages — which is what a started host,
            // resolving against its own AppContext.BaseDirectory, uses too, so no InstallRoot override is
            // needed or wanted — and, since 0.0.11-preview.88, a relative configured DirectoryPath as well,
            // because the restore composition assigns this to NuplaneBuilder.BasePath before ConfigureBuilder
            // runs. That is why this process's own current directory is left exactly as the operator had it:
            // moving it for the pass, which is what the tool used to do, no longer changes any answer.
            BasePath = target.HostDirectory,
            // Pinned rather than defaulted, because this tool reads exactly one state file
            // (NuplaneInstallRoot.DefaultStateFile) and a restore that wrote anywhere else would report
            // success and leave the very next step unable to find it.
            StateFilePath = target.StateFile,
            // D10's opt-in exception buys a populated host, not a moving target: a restored artifact must
            // equal the one a started host produces for the same pins, so anything that does not name a
            // single version refuses before a byte is downloaded.
            RequirePinnedVersions = true
        };
        if (target.HasDirectoryFeedModule)
            options.ConfigureBuilder = NuplaneDirectoryFeeds.Register;

        // Writes nothing and contacts no remote feed, so a feed whose credential reference cannot be
        // resolved is named before anything is downloaded rather than after.
        var desired = await NuplaneRestore.DescribeDesiredAsync(configuration, options, cancellationToken);
        if (desired.CredentialRefusedFeeds.Count > 0)
            return RestoreOutcome.CredentialsRefused([.. desired.CredentialRefusedFeeds], desired.StateFilePath, desired.InstallRoot);

        return Describe(await NuplaneRestore.RestoreAsync(configuration, options, cancellationToken));
    }

    private static RestoreOutcome Describe(NuplaneRestoreResult result)
    {
        // Checked again after the cycle even though the pre-flight already answered it: the two compose the
        // same provider, so disagreement is impossible by construction — and silence would be the wrong way
        // to find out otherwise.
        if (result.CredentialRefusedFeeds.Count > 0)
            return RestoreOutcome.CredentialsRefused([.. result.CredentialRefusedFeeds], result.StateFilePath, result.InstallRoot);

        if (result.Skipped)
        {
            return result.SkipReason switch
            {
                NuplaneRestoreSkipReason.UnpinnedRequests =>
                    RestoreOutcome.Unpinned([.. result.UnpinnedRequests.Select(Describe)], result.StateFilePath, result.InstallRoot),
                NuplaneRestoreSkipReason.StoreLockUnavailable =>
                    RestoreOutcome.StoreLocked(result.StateFilePath, result.InstallRoot),
                // A skip reason this build does not recognize is refused rather than guessed at: silently
                // mapping it to "lock held" would misreport why the run did nothing for whatever Nuplane
                // adds next.
                _ => RestoreOutcome.Degraded(
                    [$"Unrecognized restore skip reason '{result.SkipReason}'."], result.StateFilePath, result.InstallRoot)
            };
        }

        if (result is { IsDegraded: false, FailedPackages.Count: 0 })
            return RestoreOutcome.Restored(result.ActivePackages.Count, result.StateFilePath, result.InstallRoot);

        // A capability refusal is a failed package like any other in FailedPackages, so it would be reported
        // as "this package could not be installed" — true, and useless: the package is on the feed, and what
        // is actually missing is a decision the host has not made. The stage that says so is on the result's
        // own Refusals, which is where it is read from.
        var capability = CapabilityRefusals(result);
        return capability.Count > 0
            ? RestoreOutcome.CapabilityRefused(capability, result.StateFilePath, result.InstallRoot)
            : RestoreOutcome.Degraded([.. result.FailedPackages], result.StateFilePath, result.InstallRoot);
    }

    /// <summary>
    /// Every <c>capability-*</c> refusal this cycle recorded, as Nuplane worded it. Nuplane's own messages
    /// name the capability, every declared option and the configuration key, so they are carried verbatim
    /// rather than restated in worse words by a tool that knows less about the closure than the cycle did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from <see cref="NuplaneRestoreResult.Refusals"/>, which carries the stage and the message of every
    /// failure this cycle recorded, joined upstream on the cycle's own failed ids and its correlation id — so a
    /// failure an <em>earlier</em> cycle recorded for a package this one installed cannot be reported as if it
    /// had just happened. Until Nuplane 0.0.11-preview.92 this tool read the store state the restore had just
    /// written and did that join itself.
    /// </para>
    /// <para>
    /// A contributed request refused for want of a single-point pin reaches
    /// <see cref="NuplaneRestoreResult.UnpinnedRequests"/> too, on a non-skipped degraded result. Both say
    /// the same thing about the same package, and the refusal is the one that names the key, so this is the
    /// one read either way.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> CapabilityRefusals(NuplaneRestoreResult result) =>
    [
        .. result.Refusals
            .Where(refusal => refusal.Stage.StartsWith(CapabilityStagePrefix, StringComparison.Ordinal))
            .OrderBy(refusal => refusal.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(refusal => $"{refusal.PackageId} ({refusal.Stage}): {refusal.Message}")
    ];

    /// <summary>One unpinned request as the refusal lists it: <c>id version-request (feed)</c>.</summary>
    private static string Describe(DesiredPackageDescription request) =>
        $"{request.PackageId} {(string.IsNullOrWhiteSpace(request.VersionRange) ? "<no version>" : request.VersionRange)} " +
        $"({request.FeedName ?? request.SourceName})";
}
