using Elsa.Versioning.Calculator.Tests.Support;

namespace Elsa.Versioning.Calculator.Tests;

/// <summary>The publish gates: monotonicity (spec 150 FR-012, SC-011) and forward-only (FR-021, SC-014).</summary>
public sealed class GateTests : SyntheticHistory
{
    /// <summary>
    /// SC-011: a changed package whose computed version is not above its last published one fails, naming the package,
    /// both versions and the paths that marked it changed.
    /// </summary>
    [Fact]
    public void The_monotonicity_gate_fails_a_package_that_would_not_advance()
    {
        var ahead = RecordWith("Elsa.Tasks", "4.1.2-preview");
        Edit("src/Tasks/Scheduler.cs");
        var commit = Repo.Commit();

        var exception = Assert.Throws<VersionGateException>(() => Compute(commit, ahead));

        Assert.Contains("FR-012", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Elsa.Tasks: computed 4.0.0, last published 4.1.2-preview", exception.Message, StringComparison.Ordinal);
        Assert.Contains("src/Tasks/Scheduler.cs (changed)", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Elsa.Http", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Lowering a line's <c>major.minor</c> below what was published would publish backwards, so it fails.</summary>
    [Fact]
    public void The_monotonicity_gate_fails_a_lowered_line()
    {
        Repo.ElsaContractsVersion = "3.9";
        var commit = Repo.Commit("Lower the contracts line");

        var exception = Assert.Throws<VersionGateException>(() => Compute(commit));

        Assert.Contains("Elsa.Primitives: computed 3.9.0, last published 4.0.3-preview", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>SC-014: a re-run for a commit older than the record's latest publish refuses.</summary>
    [Fact]
    public void A_commit_older_than_the_latest_publish_is_refused()
    {
        Edit("src/Tasks/Scheduler.cs");
        Publish(CommitAndCompute());

        var exception = Assert.Throws<VersionGateException>(() => Compute(Baseline));

        Assert.Contains("FR-021", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>FR-021: the latest publish itself and every descendant of it are accepted.</summary>
    [Fact]
    public void The_latest_publish_and_its_descendants_are_accepted()
    {
        Edit("src/Tasks/Scheduler.cs");
        var published = CommitAndCompute();
        Publish(published);
        Edit("src/Http/Http.cs");

        Assert.Empty(Compute(published.Commit).Affected);
        Assert.Contains("Elsa.Http", CommitAndCompute().Affected);
    }

    /// <summary>A commit on a line of history that does not descend from the latest publish is refused.</summary>
    [Fact]
    public void A_commit_that_does_not_descend_from_the_latest_publish_is_refused()
    {
        Edit("src/Tasks/Scheduler.cs");
        Publish(CommitAndCompute());
        Repo.Git("checkout", "--quiet", "-b", "side", Baseline);
        Edit("src/Http/Http.cs");
        var side = Repo.CommitAsIs("Side change");

        Assert.Throws<VersionGateException>(() => Compute(side));
    }

    /// <summary>
    /// A rewritten <c>main</c>: the record's latest publish is no longer an ancestor of the rebuilt history, even with
    /// identical content, so publishing stops until the record is settled (FR-019) rather than renumbering silently.
    /// </summary>
    [Fact]
    public void A_rewritten_main_is_refused()
    {
        Edit("src/Tasks/Scheduler.cs");
        var published = CommitAndCompute("Change Tasks");
        Publish(published);
        Repo.Git("reset", "--quiet", "--hard", Baseline);
        Edit("src/Tasks/Scheduler.cs");
        var rewritten = Repo.CommitAsIs("Change Tasks, rewritten");

        Assert.NotEqual(published.Commit, rewritten);
        Assert.Contains("FR-021", Assert.Throws<VersionGateException>(() => Compute(rewritten)).Message, StringComparison.Ordinal);
    }

    /// <summary>A latest publish whose commit is not in the repository at all (a shallow clone, a pruned rewrite) is refused.</summary>
    [Fact]
    public void A_latest_publish_missing_from_the_repository_is_refused()
    {
        var missing = new PublishedVersions(new string('a', 40), Record.Packages);

        var exception = Assert.Throws<VersionGateException>(() => Compute(record: missing));

        Assert.Contains("not in this repository", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A record entry whose commit is missing cannot be compared against, so the computation stops.</summary>
    [Fact]
    public void A_record_entry_whose_commit_is_missing_is_refused()
    {
        var missing = new PublishedVersions(Baseline, Record.Packages.Select(entry =>
            entry.PackageId == "Elsa.Http" ? entry with { Commit = new string('b', 40) } : entry));

        var exception = Assert.Throws<InvalidOperationException>(() => Compute(record: missing));

        Assert.Contains(new string('b', 40), exception.Message, StringComparison.Ordinal);
    }
}
