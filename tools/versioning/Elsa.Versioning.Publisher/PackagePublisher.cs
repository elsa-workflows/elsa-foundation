using System.Text;
using Elsa.Versioning.Calculator;

namespace Elsa.Versioning.Publisher;

/// <summary>What became of one package of the affected set.</summary>
public enum PackageOutcomeKind
{
    /// <summary>The feed took the push, and the package is recorded.</summary>
    Pushed,

    /// <summary>
    /// The feed already held the version with the same input fingerprint: a push an earlier run made and did not
    /// record. It is recorded now (spec 150 FR-018).
    /// </summary>
    AlreadyPublished,

    /// <summary>The feed holds the version from other inputs, or its fingerprint could not be read; its copy is kept (FR-018).</summary>
    Collision,

    /// <summary>The push failed; the package is not recorded, so the next run from main pushes it again.</summary>
    Failed,

    /// <summary>Not pushed, because an earlier package failed and the publish stopped there.</summary>
    NotAttempted
}

public sealed record PackageOutcome(string PackageId, string Version, PackageOutcomeKind Kind, string Detail)
{
    /// <summary>True when the package is on the feed as computed, and so belongs in the record.</summary>
    public bool Recorded => Kind is PackageOutcomeKind.Pushed or PackageOutcomeKind.AlreadyPublished;
}

/// <param name="Plan">The plan published.</param>
/// <param name="Packages">Every package of the affected set, in the order they were pushed.</param>
/// <param name="RecordCommit">The <c>publish-state</c> commit the write-back made, or null when nothing was recorded.</param>
/// <param name="Failure">Why the publish did not complete, or null when it did.</param>
public sealed record PublishReport(PublishPlan Plan, IReadOnlyList<PackageOutcome> Packages, string? RecordCommit, string? Failure)
{
    public bool Succeeded => Failure is null;
}

/// <summary>
/// Pushes a computation's affected set to the feed and records what landed on <c>publish-state</c> (spec 150 FR-006,
/// FR-011, FR-014, FR-018): the logic of the Packages workflow's publish job, with the feed and the record behind
/// interfaces.
/// </summary>
/// <remarks>
/// <para>
/// Packages are pushed in dependency order, each after every package of the set its nuspec depends on, and the
/// publish stops at the first one that fails: a package is never pushed with a range on a version the feed does not
/// hold as computed. A push the feed rejects because the version exists is settled by the input fingerprint of the
/// feed's copy (FR-018): the same fingerprint means an earlier run pushed it and did not record it, so it is recorded
/// now and the publish goes on; another fingerprint, or none that can be read, fails the publish and leaves the feed's
/// copy alone. No push skips a duplicate (FR-011).
/// </para>
/// <para>
/// Whatever reached the feed is then recorded in one write-back, even when the publish stopped part way, and only
/// that (FR-006, FR-014): each package's entry names the version pushed and the commit built, and the record names
/// that commit as its latest publish (FR-021). A run that dies between its pushes and its write-back leaves packages
/// on the feed that the record does not name; the next run computes the same versions for them, finds them on the
/// feed with the same fingerprints, and records them, with nobody intervening.
/// </para>
/// </remarks>
public sealed class PackagePublisher(IPackageFeed feed, IPublishState state, TextWriter log)
{
    private const int VersionsShownPerPackage = 5;

    /// <param name="plan">A <see cref="PublishMode.Publish"/> or <see cref="PublishMode.Bootstrap"/> plan; its state commit is the write-back's parent.</param>
    /// <param name="computation">The computation for the plan's commit against <paramref name="record"/>.</param>
    /// <param name="record">The record at the plan's state commit, or an empty one for the bootstrap.</param>
    /// <param name="packagesDirectory">Where the pack job put the affected set's packages.</param>
    /// <exception cref="PublishRefusedException">The bootstrap's preconditions do not hold; nothing was pushed.</exception>
    /// <exception cref="InvalidOperationException">The packages are not exactly the computation's affected set; nothing was pushed.</exception>
    public async Task<PublishReport> PublishAsync(
        PublishPlan plan, VersionComputation computation, PublishedVersions record, string packagesDirectory, CancellationToken cancellationToken = default)
    {
        if (plan.Mode is not (PublishMode.Publish or PublishMode.Bootstrap))
            throw new PublishRefusedException($"A {plan.Mode.Name()} plan pushes nothing.");

        var packages = Match(computation, PackProperties.LabelFor(computation, PrereleaseLabel.MainBranch), packagesDirectory);
        if (plan.Mode == PublishMode.Bootstrap)
            await RequireBootstrapPreconditionsAsync(plan, computation, record, packages, cancellationToken);

        var outcomes = new List<PackageOutcome>();
        string? failure = null;
        foreach (var (computed, packed) in PushOrder(packages))
        {
            var outcome = failure is null
                ? await PushAsync(computed, packed, cancellationToken)
                : new PackageOutcome(computed.PackageId, packed.Version, PackageOutcomeKind.NotAttempted, "not pushed: the publish stopped at an earlier failure");
            outcomes.Add(outcome);
            log.WriteLine($"{outcome.PackageId} {outcome.Version}: {outcome.Kind}. {outcome.Detail}");
            if (outcome.Kind is PackageOutcomeKind.Collision or PackageOutcomeKind.Failed)
                failure = $"{outcome.PackageId} {outcome.Version}: {outcome.Detail}";
        }

        var recorded = outcomes.Where(outcome => outcome.Recorded).ToArray();
        if (recorded.Length == 0)
            return new PublishReport(plan, outcomes, null, failure);

        try
        {
            var commit = state.Write(Updated(record, recorded, computation.Commit), plan.StateCommit, Message(plan, recorded));
            log.WriteLine($"Recorded {recorded.Length} package(s) on {GitPublishState.DefaultBranch} as {commit}.");
            return new PublishReport(plan, outcomes, commit, failure);
        }
        catch (InvalidOperationException exception)
        {
            return new PublishReport(plan, outcomes, null,
                (failure is null ? string.Empty : failure + " ") +
                $"The write-back failed, so {recorded.Length} package(s) are on the feed but not recorded: {exception.Message} The next run from main " +
                "finds them on the feed with the same input fingerprints and records them (spec 150 FR-018).");
        }
    }

    /// <summary>
    /// Pairs every package of the affected set with its packed file, which must carry the version the set is published
    /// at and the fingerprint computed for it. A package missing, one the computation does not publish, or one packed
    /// from other inputs refuses the whole publish before anything is pushed.
    /// </summary>
    private static IReadOnlyList<(ComputedPackage Computed, PackedPackage Packed)> Match(VersionComputation computation, string? label, string directory)
    {
        var packed = (Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.nupkg") : [])
            .Order(StringComparer.Ordinal)
            .Select(PackedPackage.Read)
            .ToArray();
        var byId = packed.GroupBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, StringComparer.OrdinalIgnoreCase);
        var affected = computation.Packages.Where(package => package.Affected).ToArray();

        var problems = new List<string>();
        var pairs = new List<(ComputedPackage, PackedPackage)>();
        foreach (var computed in affected)
        {
            var version = PackProperties.VersionOf(computed, label);
            if (!byId.TryGetValue(computed.PackageId, out var files))
                problems.Add($"{computed.PackageId} {version} is to be published, but no package of it was packed");
            else if (files.Count() > 1)
                problems.Add($"{computed.PackageId} was packed more than once: {string.Join(", ", files.Select(file => Path.GetFileName(file.Path)))}");
            else if (!string.Equals(files.Single().Version, version, StringComparison.OrdinalIgnoreCase))
                problems.Add($"{computed.PackageId} was packed as {files.Single().Version}, but is to be published as {version}");
            else if (files.Single().Fingerprint != computed.Fingerprint)
                problems.Add($"{computed.PackageId} {version} carries input fingerprint {files.Single().Fingerprint}, but {computed.Fingerprint} was computed for {computation.Commit}");
            else
                pairs.Add((computed, files.Single()));
        }

        problems.AddRange(byId.Keys
            .Where(id => !affected.Any(package => string.Equals(package.PackageId, id, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .Select(id => $"{id} was packed, but the computation does not publish it (spec 150 FR-006a)"));

        return problems.Count == 0
            ? pairs
            : throw new InvalidOperationException(
                $"The packages in {directory} are not the affected set computed for {computation.Commit}, so nothing was pushed:" + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(problem => "  " + problem)));
    }

    /// <summary>
    /// The owner's gate on the first publish: no record yet, every package in the set, and no version on the feed that
    /// the bootstrap's own would not sort above. The old <c>4.0.0-preview.N</c> and branch builds sort above
    /// <c>4.0.0-preview</c> or beside it, so each must be removed from the feed first. A feed that cannot say what it
    /// holds refuses the bootstrap as surely as one that holds too much.
    /// </summary>
    private async Task RequireBootstrapPreconditionsAsync(
        PublishPlan plan, VersionComputation computation, PublishedVersions record, IReadOnlyList<(ComputedPackage Computed, PackedPackage Packed)> packages,
        CancellationToken cancellationToken)
    {
        if (plan.StateCommit is not null || record.Packages.Count > 0 || record.LastPublishCommit is not null)
            throw new PublishRefusedException(
                $"The bootstrap creates the record, but one exists at {plan.StateCommit ?? "an unnamed revision"}; the bootstrap is one-off (spec 150 FR-014).");

        var left = computation.Packages.Where(package => !package.Affected).Select(package => package.PackageId).ToArray();
        if (left.Length > 0)
            throw new PublishRefusedException($"The bootstrap publishes every package, but the computation leaves out {string.Join(", ", left)}.");

        var blockers = new List<string>();
        foreach (var (computed, _) in packages)
        {
            IReadOnlyList<string> versions;
            try
            {
                versions = await feed.ListVersionsAsync(computed.PackageId, cancellationToken);
            }
            catch (FeedException exception)
            {
                throw new PublishRefusedException(
                    $"The bootstrap refuses: the feed could not list the versions of {computed.PackageId}, so nothing shows that no old version remains. {exception.Message}");
            }

            var blocking = versions.Where(version => !SortsBelow(version, computed.Version)).ToArray();
            if (blocking.Length > 0)
                blockers.Add($"  {computed.PackageId}: {string.Join(", ", blocking.Take(VersionsShownPerPackage))}" +
                             (blocking.Length > VersionsShownPerPackage ? $" and {blocking.Length - VersionsShownPerPackage} more" : string.Empty));
        }

        if (blockers.Count > 0)
            throw new PublishRefusedException(
                $"The bootstrap refuses: the feed still holds versions of {blockers.Count} package(s) that do not sort below the versions it would publish, " +
                "such as the old 4.0.0-preview.N and branch builds. Nothing was pushed. Remove them from the feed, then run the bootstrap again:" +
                Environment.NewLine + string.Join(Environment.NewLine, blockers));
    }

    /// <summary>True when a feed version's <c>major.minor.patch</c> is below <paramref name="published"/>'s; one that cannot be read is not.</summary>
    private static bool SortsBelow(string version, PackageVersionNumber published)
    {
        try
        {
            return PackageVersionNumber.Parse(version, "feed").CompareNumeric(published) < 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<PackageOutcome> PushAsync(ComputedPackage computed, PackedPackage packed, CancellationToken cancellationToken)
    {
        PackageOutcome Outcome(PackageOutcomeKind kind, string detail) => new(computed.PackageId, packed.Version, kind, detail);

        var pushed = await feed.PushAsync(packed.Path, cancellationToken);
        if (pushed.Status == PushStatus.Pushed)
            return Outcome(PackageOutcomeKind.Pushed, $"pushed ({pushed.Detail})");
        if (pushed.Status == PushStatus.Failed)
            return Outcome(PackageOutcomeKind.Failed, $"the push failed ({pushed.Detail}); it is not recorded, so the next run from main pushes it again");

        string onFeed;
        try
        {
            onFeed = await feed.ReadFingerprintAsync(computed.PackageId, packed.Version, cancellationToken);
        }
        catch (FeedException exception)
        {
            return Outcome(PackageOutcomeKind.Collision,
                $"the feed already holds {packed.Version}, and its input fingerprint cannot be read ({exception.Message}), so nothing shows it was built from " +
                $"these inputs, {computed.Fingerprint}. The feed's copy is kept (spec 150 FR-018); settle the record with the repair workflow (FR-019).");
        }

        return onFeed == computed.Fingerprint
            ? Outcome(PackageOutcomeKind.AlreadyPublished,
                $"the feed already held {packed.Version} with the same input fingerprint, {onFeed}: an earlier run pushed it and did not record it (spec 150 FR-018)")
            : Outcome(PackageOutcomeKind.Collision,
                $"the feed already holds {packed.Version}, built from other inputs: the feed's copy has input fingerprint {onFeed}, this one {computed.Fingerprint}. " +
                "The feed's copy is kept (spec 150 FR-018); settle the record with the repair workflow (FR-019).");
    }

    /// <summary>The set in dependency order, each package after every package of the set its nuspec depends on; ties by id.</summary>
    private static IReadOnlyList<(ComputedPackage Computed, PackedPackage Packed)> PushOrder(IReadOnlyList<(ComputedPackage Computed, PackedPackage Packed)> packages)
    {
        var byId = packages.ToDictionary(package => package.Computed.PackageId, StringComparer.OrdinalIgnoreCase);
        var waitingOn = packages.ToDictionary(
            package => package.Computed.PackageId,
            package => package.Packed.DependencyIds.Where(byId.ContainsKey).Select(id => byId[id].Computed.PackageId).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var ready = new SortedSet<string>(StringComparer.Ordinal);
        var order = new List<(ComputedPackage, PackedPackage)>();

        while (true)
        {
            ready.UnionWith(waitingOn.Where(entry => entry.Value.Count == 0).Select(entry => entry.Key));
            if (ready.Min is not { } next)
                break;

            ready.Remove(next);
            waitingOn.Remove(next);
            order.Add(byId[next]);
            foreach (var dependencies in waitingOn.Values)
                dependencies.Remove(next);
        }

        return waitingOn.Count == 0
            ? order
            : throw new InvalidOperationException($"The packages {string.Join(", ", waitingOn.Keys.Order(StringComparer.Ordinal))} depend on each other in a cycle; nothing was pushed.");
    }

    /// <summary>The record with an entry for each recorded package, and this commit as its latest publish; no entry is removed (FR-014).</summary>
    private static PublishedVersions Updated(PublishedVersions record, IReadOnlyList<PackageOutcome> recorded, string commit)
    {
        var ids = recorded.Select(outcome => outcome.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new PublishedVersions(commit, record.Packages
            .Where(entry => !ids.Contains(entry.PackageId))
            .Concat(recorded.Select(outcome => new PublishedPackage(outcome.PackageId, PackageVersionNumber.Parse(outcome.Version, outcome.PackageId), commit))));
    }

    private static string Message(PublishPlan plan, IReadOnlyList<PackageOutcome> recorded)
    {
        var message = new StringBuilder(plan.Mode == PublishMode.Bootstrap
                ? $"Bootstrap the last-published record at {plan.Commit}"
                : $"Record the publish of {plan.Commit}")
            .Append("\n\n")
            .Append($"{recorded.Count} package(s) recorded, built from main at {plan.Commit}:\n");
        foreach (var outcome in recorded)
            message.Append($"  {outcome.PackageId} {outcome.Version}{(outcome.Kind == PackageOutcomeKind.AlreadyPublished ? " (already on the feed with the same inputs)" : string.Empty)}\n");

        return message.ToString();
    }
}
