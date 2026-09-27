namespace Elsa.Versioning.Calculator;

/// <summary>A publish gate refused the computation: nothing computed may be published (spec 150 FR-012, FR-021).</summary>
public sealed class VersionGateException(string message) : Exception(message);

/// <summary>
/// Computes every package's version, the affected set and each package's input fingerprint for one commit against
/// one revision of the last-published record (spec 150).
/// </summary>
/// <remarks>
/// <para>
/// The result is a function of the commit and the record alone (FR-009): history is read only from local git objects
/// and the dependency map committed in them, never from the working tree, a clock, the environment or a feed
/// (FR-006, FR-013). The record is an input beside the commit, never part of any tree compared, so no revision of it
/// can mark a package changed (FR-016).
/// </para>
/// <para>
/// A package is changed when its package-affecting inputs at the commit being built differ from those at the commit
/// its record entry names (<see cref="PackageInputs"/>); when it has no entry; when its line's <c>major.minor</c> is
/// not the one it was last published on; when it changed version line; or when a <see cref="ForcedAdvance"/> names it
/// (FR-003). On top of that:
/// </para>
/// <list type="bullet">
/// <item>Line A moves as one: any changed member moves every member to one past the line's last published patch (FR-002),
/// and a line a publish left partly pushed is completed at the version it was pushed at.</item>
/// <item>A package whose major advances moves every package that references it, because every published range stops
/// below the next major (FR-007; ADR 0067, "only a major change propagates through the reverse closure").</item>
/// <item>A tool package moves whenever a package it carries moves, since it carries that package's build rather than
/// a range over it.</item>
/// </list>
/// <para>
/// A changed package's patch is one past its recorded patch, or 0 when it has no record on its line's current
/// <c>major.minor</c>. An unchanged package keeps its recorded version and is not published.
/// </para>
/// </remarks>
public static class VersionCalculator
{
    private const int ReasonsShownPerPackage = 20;

    /// <param name="git">The repository.</param>
    /// <param name="revision">The commit being built.</param>
    /// <param name="record">The revision of the last-published record to compute against.</param>
    /// <param name="forced">Packages to advance although their inputs did not change (FR-003), or null.</param>
    /// <exception cref="VersionGateException">The forward-only (FR-021) or monotonicity (FR-012) gate refused.</exception>
    /// <exception cref="InvalidOperationException">
    /// An input could not be read, or does not describe the repository, or <paramref name="forced"/> names a package
    /// the commit does not have.
    /// </exception>
    public static VersionComputation Compute(GitRepository git, string revision, PublishedVersions record, ForcedAdvance? forced = null)
    {
        var commit = git.ResolveCommit(revision);
        RequireForwardOnly(git, commit, record);

        var objects = new ObjectCache(git);
        var built = new CommitSnapshot(objects, commit);
        var snapshots = new Dictionary<string, CommitSnapshot>(StringComparer.Ordinal) { [commit] = built };
        CommitSnapshot Snapshot(string at)
        {
            if (snapshots.TryGetValue(at, out var snapshot))
                return snapshot;

            if (!git.HasCommit(at))
                throw new InvalidOperationException(
                    $"The record names {at} as a last-published commit, but it is not in this repository. Fetch full history (a shallow " +
                    "clone lacks it); if main was rewritten, the entry has to be settled with the repair workflow (spec 150 FR-019).");

            return snapshots[at] = new CommitSnapshot(objects, at);
        }

        var lines = VersionLineSettings.Parse(built.ReadFile(VersionLineSettings.RelativePath), $"{VersionLineSettings.RelativePath} at {commit}");
        RequireMapDescribesTree(built, lines);

        var packages = built.Map.Packable.Values
            .OrderBy(node => node.PackageId, StringComparer.Ordinal)
            .Select(node => new Candidate(node, built.InputsOf(node), record.Find(node.PackageId!)))
            .ToArray();
        RequireVersionLinesSetInOnePlace(built, packages);

        var forcedIds = RequireForcedPackagesExist(built, forced);
        foreach (var package in packages)
            package.Reasons.AddRange(OwnChanges(package, lines, Snapshot));

        foreach (var package in packages.Where(package => forcedIds.Contains(package.Node.PackageId!)))
            package.Reasons.Add(ForcedAdvance.ReasonPrefix + forced!.Reason);

        Assign(packages, lines, Snapshot);
        while (Propagate(built, packages))
            Assign(packages, lines, Snapshot);

        RequireMonotonic(packages);

        return new VersionComputation(
            commit,
            record.LastPublishCommit,
            lines,
            packages.Select(package => new ComputedPackage(
                package.Node.PackageId!,
                package.Node.Path,
                package.Node.Line!,
                package.Version,
                package.Affected,
                package.Record,
                package.Inputs.Fingerprint,
                package.Affected ? [.. package.Reasons, .. package.LineReasons] : [])).ToArray());
    }

    /// <summary>
    /// FR-021: a publish only moves forward along <c>main</c>. Building older content against a newer record would
    /// compare it with inputs it never had and could publish it at a higher version.
    /// </summary>
    private static void RequireForwardOnly(GitRepository git, string commit, PublishedVersions record)
    {
        if (record.LastPublishCommit is not { } latest)
            return;

        if (!git.HasCommit(latest))
            throw new VersionGateException(
                $"Forward-only gate (spec 150 FR-021): the record's latest publish {latest} is not in this repository, so nothing proves " +
                $"{commit} descends from it. Fetch full history (a shallow clone cannot prove ancestry); if main was rewritten, the " +
                "record has to be settled to name a commit of the new history before anything publishes.");

        if (!git.IsAncestorOrSelf(latest, commit))
            throw new VersionGateException(
                $"Forward-only gate (spec 150 FR-021): {commit} is neither the record's latest publish {latest} nor a descendant of it. " +
                "A publish only moves forward along main; computing older content against a newer record could publish it at a higher version.");
    }

    /// <summary>
    /// The dependency map committed with the commit being built must describe that commit's projects, or a project
    /// added without regenerating it would own nothing and never publish, and a changed Line A list would put packages
    /// on the wrong line.
    /// </summary>
    private static void RequireMapDescribesTree(CommitSnapshot built, VersionLineSettings lines)
    {
        var inTree = built.ProjectFiles().ToHashSet(StringComparer.Ordinal);
        var inMap = built.Map.Nodes.Select(node => node.Path).ToHashSet(StringComparer.Ordinal);
        var unmapped = inTree.Except(inMap).Order(StringComparer.Ordinal).ToArray();
        var missing = inMap.Except(inTree).Order(StringComparer.Ordinal).ToArray();
        if (unmapped.Length + missing.Length > 0)
            throw new InvalidOperationException(
                $"{DependencyMapDataset.RelativePath} at {built.Commit} does not describe the projects in that commit" +
                Listed("; projects it lacks", unmapped) + Listed("; nodes with no project", missing) +
                ". Regenerate it with: dotnet run --project tools/maps/Elsa.Maps.Generator -- all");

        var lineA = built.Map.Packable.Values.Where(node => node.Line == "A").Select(node => node.Name).ToHashSet(StringComparer.Ordinal);
        if (!lineA.SetEquals(lines.LineAMembers))
            throw new InvalidOperationException(
                $"{DependencyMapDataset.RelativePath} at {built.Commit} puts {Listed(string.Empty, [.. lineA.Order(StringComparer.Ordinal)])} on Line A, " +
                $"but {VersionLineSettings.RelativePath} lists {Listed(string.Empty, [.. lines.LineAMembers.Order(StringComparer.Ordinal)])}. " +
                "Regenerate it with: dotnet run --project tools/maps/Elsa.Maps.Generator -- all");
    }

    /// <summary>
    /// FR-010: each line's <c>major.minor</c> is defined in one place. The calculator reads it from
    /// <c>VersionLines.props</c>, so a build file that assigns it anywhere else would make the build and the
    /// calculator disagree.
    /// </summary>
    private static void RequireVersionLinesSetInOnePlace(CommitSnapshot built, IEnumerable<Candidate> packages)
    {
        var elsewhere = packages
            .SelectMany(package => package.Inputs.Entries.Keys)
            .Where(key => key.StartsWith(PackageInputs.FilePrefix, StringComparison.Ordinal))
            .Select(key => key[PackageInputs.FilePrefix.Length..])
            .Where(path => path != VersionLineSettings.RelativePath)
            .Distinct(StringComparer.Ordinal)
            .Where(path => RepositoryPath.IsMsBuildFile(path) && built.BuildFile(path) is { } file &&
                           file.SetProperties.Any(MsBuildFile.VersionLineProperties.Contains))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (elsewhere.Length > 0)
            throw new InvalidOperationException(
                $"{Listed(string.Empty, elsewhere)} at {built.Commit} assign(s) a version-line property; only {VersionLineSettings.RelativePath} may (spec 150 FR-010).");
    }

    /// <summary>
    /// The package ids a force-advance names. One the commit does not have is refused rather than skipped: a misspelt
    /// id would otherwise advance nothing, and the publish would look as if it had done what was asked.
    /// </summary>
    private static IReadOnlySet<string> RequireForcedPackagesExist(CommitSnapshot built, ForcedAdvance? forced)
    {
        var ids = new HashSet<string>(forced?.PackageIds ?? [], StringComparer.OrdinalIgnoreCase);
        var unknown = ids.Where(id => !built.Map.Packable.ContainsKey(id)).Order(StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
            throw new InvalidOperationException(
                $"The force-advance names {Listed(string.Empty, unknown)}, which no packable project at {built.Commit} has as its package id.");

        return ids;
    }

    /// <summary>Why a package counts as changed on its own account, before the line and propagation rules.</summary>
    private static IEnumerable<string> OwnChanges(Candidate package, VersionLineSettings lines, Func<string, CommitSnapshot> snapshot)
    {
        if (package.Record is not { } record)
        {
            yield return "no last-published record: first publish of this package id";
            yield break;
        }

        var line = lines.For(package.Node.Line!);
        if (record.Version.Line != line)
            yield return $"line {package.Node.Line} is on {line}; the last published version {record.Version} is on {record.Version.Line}";

        if (!snapshot(record.Commit).Map.Packable.TryGetValue(package.Node.PackageId!, out var then))
        {
            yield return $"no project had package id {package.Node.PackageId} at its last-published commit {record.Commit}";
            yield break;
        }

        if (then.Line != package.Node.Line)
            yield return $"moved from line {then.Line} to line {package.Node.Line}";

        foreach (var difference in PackageInputs.Differences(snapshot(record.Commit).InputsOf(then), package.Inputs))
            yield return difference;
    }

    /// <summary>Assigns every package's version and affected state from the reasons gathered so far.</summary>
    private static void Assign(IReadOnlyList<Candidate> packages, VersionLineSettings lines, Func<string, CommitSnapshot> snapshot)
    {
        foreach (var package in packages)
        {
            var line = lines.For(package.Node.Line!);
            package.LineReasons.Clear();
            package.Affected = package.Reasons.Count > 0;
            package.Version = !package.Affected ? package.Record!.Version with { Label = null }
                : new PackageVersionNumber(line.Major, line.Minor, package.Record is { } record && record.Version.Line == line ? record.Version.Patch + 1 : 0, null);
        }

        AssignLineA([.. packages.Where(package => package.Node.Line == "A")], lines.LineA, snapshot);
    }

    /// <summary>
    /// Line A moves as one (FR-002): while its members stand at one version with nothing changed, it keeps it; once any
    /// member changes, every member moves to one past the line's highest published patch.
    /// </summary>
    /// <remarks>
    /// Between the two sits a line a publish left partly pushed: some members recorded at the line's version, others
    /// still behind it. When nothing changed since that publish, the members behind complete the line at the same
    /// version rather than moving it again, as a failed push of any other package is retried at the version it failed
    /// at (spec 150, Edge Cases). Completing it needs the members at the top to share the one publish commit, and each
    /// member behind to have, now, exactly the inputs it had then; anything else moves the line.
    /// </remarks>
    private static void AssignLineA(IReadOnlyList<Candidate> members, LineVersion line, Func<string, CommitSnapshot> snapshot)
    {
        var onLine = members.Where(member => member.Record is { } record && record.Version.Line == line).ToArray();
        var topPatch = onLine.Length > 0 ? onLine.Max(member => member.Record!.Version.Patch) : -1;
        var atTop = onLine.Where(member => member.Record!.Version.Patch == topPatch).ToArray();
        var behind = members.Except(atTop).ToArray();
        var changed = atTop.Where(member => member.Reasons.Count > 0).Select(member => member.Node.PackageId!).ToArray();
        var publishCommits = atTop.Select(member => member.Record!.Commit).Distinct(StringComparer.Ordinal).ToArray();

        if (changed.Length == 0 && behind.Length == 0)
            return;

        if (changed.Length == 0 && publishCommits.Length == 1 && behind.All(member => HasInputsOf(member, snapshot(publishCommits[0]))))
        {
            foreach (var member in behind)
            {
                member.Affected = true;
                member.Version = new PackageVersionNumber(line.Major, line.Minor, topPatch, null);
                member.LineReasons.Add($"completes Line A at {member.Version}, where its other members were published at {publishCommits[0]} with these same inputs");
            }

            return;
        }

        var cause = changed.Length > 0 ? $"changed: {string.Join(", ", changed)}"
            : onLine.Length == 0 ? $"no member was published on {line} yet"
            : $"not every member stands at {line}.{topPatch} with the inputs it was published with: {string.Join(", ", behind.Select(member => member.Node.PackageId))}";
        foreach (var member in members)
        {
            member.Affected = true;
            member.Version = new PackageVersionNumber(line.Major, line.Minor, topPatch + 1, null);
            if (member.Reasons.Count == 0)
                member.LineReasons.Add($"Line A moves as one; {cause}");
        }
    }

    /// <summary>True when the package's inputs now are exactly its inputs at <paramref name="then"/>.</summary>
    private static bool HasInputsOf(Candidate package, CommitSnapshot then) =>
        then.Map.Packable.TryGetValue(package.Node.PackageId!, out var node) && node.Line == package.Node.Line &&
        PackageInputs.Differences(then.InputsOf(node), package.Inputs).Count == 0;

    /// <summary>
    /// Marks the packages the propagation rules reach: dependents of a package whose major advances, and tools that
    /// carry a package that advances. Returns whether any was newly marked.
    /// </summary>
    private static bool Propagate(CommitSnapshot built, IReadOnlyList<Candidate> packages)
    {
        var byPath = packages.ToDictionary(package => package.Node.Path, StringComparer.Ordinal);
        var marked = false;
        foreach (var package in packages.Where(package => !package.Affected))
        {
            var majors = package.Node.Edges
                .Where(edge => edge.Internal)
                .Select(edge => byPath.GetValueOrDefault(edge.Path!))
                .OfType<Candidate>()
                .Where(target => target.Affected && target.Record is { } record && target.Version.Major != record.Version.Major)
                .Select(target => $"references {target.Node.PackageId}, which moves from major {target.Record!.Version.Major} to {target.Version.Major}; every published range stops below the next major (FR-007)");
            var carried = built.PacksAsTool(package.Node)
                ? built.ReferenceClosure(package.Node)
                    .Select(project => byPath.GetValueOrDefault(project.Path))
                    .OfType<Candidate>()
                    .Where(target => target != package && target.Affected)
                    .Select(target => $"carries {target.Node.PackageId}, which advances; a tool package holds the builds of what it references")
                : [];

            var reasons = majors.Concat(carried).ToArray();
            if (reasons.Length == 0)
                continue;

            package.Reasons.AddRange(reasons);
            marked = true;
        }

        return marked;
    }

    /// <summary>
    /// FR-012: every changed package must advance past its last published version, and every unchanged one must keep
    /// it. The first holds by construction; the gate is what makes a lowered <c>major.minor</c>, a line move or a
    /// corrupted record fail instead of publishing backwards.
    /// </summary>
    private static void RequireMonotonic(IEnumerable<Candidate> packages)
    {
        var violations = packages
            .Where(package => package.Record is { } record && (package.Affected ? package.Version.CompareNumeric(record.Version) <= 0 : package.Version.CompareNumeric(record.Version) != 0))
            .Select(package =>
            {
                var reasons = package.Reasons.Concat(package.LineReasons).ToArray();
                return $"  {package.Node.PackageId}: computed {package.Version}, last published {package.Record!.Version}; " +
                       (package.Affected ? "changed by " + string.Join("; ", reasons.Take(ReasonsShownPerPackage)) + (reasons.Length > ReasonsShownPerPackage ? $"; and {reasons.Length - ReasonsShownPerPackage} more" : string.Empty) : "unchanged");
            })
            .ToArray();
        if (violations.Length > 0)
            throw new VersionGateException(
                $"Monotonicity gate (spec 150 FR-012): {violations.Length} package(s) would not advance past their last published version:" +
                Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static string Listed(string label, IReadOnlyList<string> items) =>
        items.Count == 0 ? string.Empty : $"{label}{(label.Length > 0 ? ": " : string.Empty)}{string.Join(", ", items.Take(10))}{(items.Count > 10 ? $" and {items.Count - 10} more" : string.Empty)}";

    private sealed class Candidate(MapNode node, PackageInputs inputs, PublishedPackage? record)
    {
        public MapNode Node { get; } = node;

        public PackageInputs Inputs { get; } = inputs;

        public PublishedPackage? Record { get; } = record;

        /// <summary>Why the package changed: its own inputs, its record, or a propagation rule.</summary>
        public List<string> Reasons { get; } = [];

        /// <summary>Why it moves with Line A when nothing of its own changed.</summary>
        public List<string> LineReasons { get; } = [];

        public bool Affected { get; set; }

        public PackageVersionNumber Version { get; set; }
    }
}
