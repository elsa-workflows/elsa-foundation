using Elsa.Versioning.Calculator;

namespace Elsa.Versioning.Publisher;

/// <param name="Repository">A directory inside the repository.</param>
/// <param name="Ref">The ref the repair is run from, as <c>GITHUB_REF</c> gives it; only main may write <c>publish-state</c> (spec 150 FR-019).</param>
/// <param name="PackageId">The package id whose record entry is settled against the feed.</param>
/// <param name="Reason">Why the repair is being made; the write-back commit names it.</param>
/// <param name="Actor">Who ran the repair (<c>github.actor</c>); the write-back commit names it.</param>
/// <param name="Summary">The step's <c>GITHUB_STEP_SUMMARY</c> file, or null.</param>
public sealed record RepairPackageOptions(string Repository, string Ref, string PackageId, string Reason, string Actor, string? Summary);

/// <param name="Repository">A directory inside the repository.</param>
/// <param name="Ref">The ref the repair is run from, as <c>GITHUB_REF</c> gives it; only main may write <c>publish-state</c> (spec 150 FR-019).</param>
/// <param name="Commit">Main's tip, which <paramref name="ResetTo"/> must be an ancestor of or equal to.</param>
/// <param name="ResetTo">The commit <c>last_publish_commit</c> is reset to.</param>
/// <param name="Reason">Why the repair is being made; the write-back commit names it.</param>
/// <param name="Actor">Who ran the repair (<c>github.actor</c>); the write-back commit names it.</param>
/// <param name="Summary">The step's <c>GITHUB_STEP_SUMMARY</c> file, or null.</param>
public sealed record RepairLastPublishCommitOptions(string Repository, string Ref, string Commit, string ResetTo, string Reason, string Actor, string? Summary);

/// <summary>What a repair wrote to <c>publish-state</c>, and the message its commit carries.</summary>
public sealed record RepairReport(string RecordCommit, string Message);

/// <summary>
/// Settles by hand what a publish cannot settle itself (spec 150 FR-019): a genuine collision — the feed holds a
/// version built from inputs the record does not know — or a rewritten <c>main</c>, where the record's
/// <c>last_publish_commit</c> no longer names a commit on its history (FR-021's edge case). Besides a publish and the
/// bootstrap, this is the record's only other writer, and it writes the same way: a non-forced push onto the tip it
/// read (<see cref="IPublishState.Write"/>), so it fails loudly rather than overwriting a write that landed while it
/// ran. The workflow joins the <c>publish-state</c> concurrency group beside every run from <c>main</c> (FR-020).
/// </summary>
public static class RepairCommand
{
    /// <summary>
    /// Settles a package's record entry from the feed: the highest version the feed holds of it and that version's
    /// source commit — the nuspec's <c>repository</c> <c>commit</c> (FR-018) — refusing to lower the entry (FR-012).
    /// </summary>
    /// <exception cref="PublishRefusedException">
    /// Not run from <c>main</c>, <c>publish-state</c> does not exist yet, the feed holds no version of the package, its
    /// source commit cannot be read, or settling it would lower the record's entry.
    /// </exception>
    public static async Task<RepairReport> RepairPackageAsync(
        RepairPackageOptions options, IPackageFeed feed, IPublishState state, TextWriter log, CancellationToken cancellationToken = default)
    {
        RequireMain(options.Ref);
        var (tip, record) = ReadRecord(state);

        var versions = await feed.ListVersionsAsync(options.PackageId, cancellationToken);
        if (versions.Count == 0)
            throw new PublishRefusedException($"The feed holds no version of {options.PackageId}; there is nothing to settle its record entry against.");

        var (version, number) = versions
            .Select(text => (Text: text, Number: PackageVersionNumber.Parse(text, $"a feed version of {options.PackageId}")))
            .OrderByDescending(candidate => candidate.Number, NumericOrder)
            .First();

        var current = record.Find(options.PackageId);
        if (current is not null && number.CompareNumeric(current.Version) < 0)
            throw new PublishRefusedException(
                $"The feed's highest version of {options.PackageId}, {version}, is lower than the record's entry, {current.Version}. " +
                "A repair never lowers an entry (spec 150 FR-012); nothing was written.");

        string commit;
        try
        {
            commit = await feed.ReadSourceCommitAsync(options.PackageId, version, cancellationToken);
        }
        catch (FeedException exception)
        {
            throw new PublishRefusedException($"{options.PackageId} {version}'s source commit could not be read from the feed: {exception.Message}");
        }

        if (!GitRepository.IsFullObjectId(commit))
            throw new PublishRefusedException($"{options.PackageId} {version} on the feed carries no readable source commit; nothing was written.");

        var updated = new PublishedVersions(record.LastPublishCommit, record.Packages
            .Where(entry => !string.Equals(entry.PackageId, options.PackageId, StringComparison.OrdinalIgnoreCase))
            .Append(new PublishedPackage(options.PackageId, number, commit)));

        var message =
            $"Repair {options.PackageId}'s record entry\n\n" +
            $"Package: {options.PackageId}\n" +
            $"Old: {(current is null ? "(none)" : $"{current.Version} at {current.Commit}")}\n" +
            $"New: {number} at {commit}\n" +
            $"Reason: {options.Reason}\n" +
            $"Repaired by: {options.Actor}\n";

        var written = state.Write(updated, tip, message);
        log.WriteLine(message);
        if (options.Summary is { } summary)
            await File.AppendAllTextAsync(summary, JobSummary.Repair(message, written), cancellationToken);

        return new RepairReport(written, message);
    }

    /// <summary>
    /// Resets <c>last_publish_commit</c> after a rewritten <c>main</c>, refusing a commit that is not on <c>main</c>'s
    /// history at the commit the workflow was run from.
    /// </summary>
    /// <exception cref="PublishRefusedException">Not run from <c>main</c>, <c>publish-state</c> does not exist yet, or the named commit is not on main.</exception>
    public static async Task<RepairReport> RepairLastPublishCommitAsync(
        RepairLastPublishCommitOptions options, IPublishState state, TextWriter log, CancellationToken cancellationToken = default)
    {
        RequireMain(options.Ref);
        var (tip, record) = ReadRecord(state);

        var git = new GitRepository(options.Repository);
        var commit = git.ResolveCommit(options.Commit);
        var resetTo = git.ResolveCommit(options.ResetTo);
        if (!git.IsAncestorOrSelf(resetTo, commit))
            throw new PublishRefusedException($"{options.ResetTo} ({resetTo}) is not on main ({commit}); nothing was written.");

        var updated = new PublishedVersions(resetTo, record.Packages);
        var message =
            "Repair last_publish_commit\n\n" +
            $"Old: {record.LastPublishCommit}\n" +
            $"New: {resetTo}\n" +
            $"Reason: {options.Reason}\n" +
            $"Repaired by: {options.Actor}\n";

        var written = state.Write(updated, tip, message);
        log.WriteLine(message);
        if (options.Summary is { } summary)
            await File.AppendAllTextAsync(summary, JobSummary.Repair(message, written), cancellationToken);

        return new RepairReport(written, message);
    }

    private static readonly IComparer<PackageVersionNumber> NumericOrder = Comparer<PackageVersionNumber>.Create((left, right) => left.CompareNumeric(right));

    private static void RequireMain(string reference)
    {
        if (reference != PublishPlan.MainRef)
            throw new PublishRefusedException($"The repair workflow runs from main only (spec 150 FR-019); this is {reference}.");
    }

    private static (string Tip, PublishedVersions Record) ReadRecord(IPublishState state)
    {
        var tip = state.FetchTip() ?? throw new PublishRefusedException(
            $"{GitPublishState.DefaultBranch} does not exist yet; there is nothing to repair before the bootstrap.");
        return (tip, state.Read(tip));
    }
}
