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
            // Every Nuplane path default resolves under the host rather than under this tool: the install
            // root at BasePath/.nuplane/packages — which is what a started host, resolving against its own
            // AppContext.BaseDirectory, uses too, so no InstallRoot override is needed or wanted.
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

        // A host resolves a directory feed's configured path against its own current directory, which for a
        // published host is the directory --host names. This process's current directory is the operator's,
        // so it is moved for the duration of the restore and put back afterwards; every other path this run
        // uses is already absolute.
        var restoringFrom = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(target.HostDirectory);
        try
        {
            // Writes nothing and contacts no remote feed, so a feed that configures credentials — which
            // Nuplane cannot resolve — is named before anything is downloaded rather than after.
            var desired = await NuplaneRestore.DescribeDesiredAsync(configuration, options, cancellationToken);
            if (desired.CredentialRefusedFeeds.Count > 0)
                return RestoreOutcome.CredentialsRefused([.. desired.CredentialRefusedFeeds], desired.StateFilePath, desired.InstallRoot);

            var result = await NuplaneRestore.RestoreAsync(configuration, options, cancellationToken);
            return Describe(result);
        }
        finally
        {
            Directory.SetCurrentDirectory(restoringFrom);
        }
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
            return result.SkipReason == NuplaneRestoreSkipReason.UnpinnedRequests
                ? RestoreOutcome.Unpinned([.. result.UnpinnedRequests.Select(Describe)], result.StateFilePath, result.InstallRoot)
                : RestoreOutcome.StoreLocked(result.StateFilePath, result.InstallRoot);
        }

        return result is { IsDegraded: false, FailedPackages.Count: 0 }
            ? RestoreOutcome.Restored(result.ActivePackages.Count, result.StateFilePath, result.InstallRoot)
            : RestoreOutcome.Degraded([.. result.FailedPackages], result.StateFilePath, result.InstallRoot);
    }

    /// <summary>One unpinned request as the refusal lists it: <c>id version-request (feed)</c>.</summary>
    private static string Describe(DesiredPackageDescription request) =>
        $"{request.PackageId} {(string.IsNullOrWhiteSpace(request.VersionRange) ? "<no version>" : request.VersionRange)} " +
        $"({request.FeedName ?? request.SourceName})";
}
