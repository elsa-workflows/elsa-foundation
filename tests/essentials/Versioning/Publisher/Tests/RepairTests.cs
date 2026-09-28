using Elsa.Versioning.Calculator;
using Elsa.Versioning.Publisher.Tests.Support;

namespace Elsa.Versioning.Publisher.Tests;

/// <summary>
/// The repair command (spec 150 FR-019): settles a genuine collision from the feed, refusing to lower an entry
/// (FR-012), and resets <c>last_publish_commit</c> after a rewritten <c>main</c>, refusing a commit that is not on
/// it. Besides a publish and the bootstrap, this is <c>publish-state</c>'s only other writer, and it writes onto the
/// tip it read the same way (FR-017).
/// </summary>
public sealed class RepairTests : PublishingHistory
{
    private const string PackageId = "Elsa.Tasks";

    [Fact]
    public async Task Repair_refuses_before_the_record_exists()
    {
        var refused = await Assert.ThrowsAsync<PublishRefusedException>(() => RepairPackageAsync(PackageId));

        Assert.Contains("does not exist yet", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repair_refuses_off_main()
    {
        await BootstrapAsync();

        var refused = await Assert.ThrowsAsync<PublishRefusedException>(() => RepairPackageAsync(PackageId, reference: "refs/heads/feature"));

        Assert.Contains("main only", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repair_refuses_a_package_the_feed_holds_no_version_of()
    {
        await BootstrapAsync();

        var refused = await Assert.ThrowsAsync<PublishRefusedException>(() => RepairPackageAsync("Elsa.NoSuchPackage"));

        Assert.Contains("holds no version", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>FR-012: a repair never lowers an entry, even when the feed genuinely holds something lower.</summary>
    [Fact]
    public async Task Repair_refuses_to_lower_the_entry()
    {
        await BootstrapAsync();
        SetRecordEntry(PackageId, "4.0.9-preview", Repo.Head);

        var refused = await Assert.ThrowsAsync<PublishRefusedException>(() => RepairPackageAsync(PackageId));

        Assert.Contains("lower", refused.Message, StringComparison.Ordinal);
        Assert.Equal("4.0.9-preview", RecordedVersions[PackageId]);
    }

    /// <summary>
    /// #2083's acceptance: a genuine collision — the feed holds a version built from inputs the record no longer
    /// names, here because that package's own entry was lost — is settled by one repair run naming the package, the
    /// old and new entries, the reason and who ran it; the next publish then succeeds at one past the feed's version.
    /// </summary>
    [Fact]
    public async Task A_repair_settles_a_collision_and_the_next_publish_succeeds_one_past_the_feeds_version()
    {
        var bootstrapCommit = (await BootstrapAsync()).Plan.Commit;
        Edit("src/Tasks/Scheduler.cs");
        var pushedCommit = CommitAndPush();
        Assert.True((await RunAsync()).Report!.Succeeded);
        Assert.Equal("4.0.1-preview", RecordedVersions[PackageId]);

        // Simulate a lost record entry (spec 150, Edge Cases): Elsa.Tasks's own entry reverts to the bootstrap, while
        // the feed still holds what was actually pushed and last_publish_commit still names the real latest publish.
        SetRecordEntry(PackageId, "4.0.0-preview", bootstrapCommit);

        Edit("src/Tasks/Scheduler.cs");
        CommitAndPush();
        var collided = await RunAsync();

        Assert.False(collided.Report!.Succeeded);
        Assert.Equal(PackageOutcomeKind.Collision, collided.Report.Packages.Single(package => package.PackageId == PackageId).Kind);
        Assert.Equal("4.0.0-preview", RecordedVersions[PackageId]);

        var repaired = await RepairPackageAsync(PackageId, reason: "the record's Elsa.Tasks entry was lost", actor: "sfmskywalker");

        Assert.Equal(("4.0.1-preview", pushedCommit), (RecordedVersions[PackageId], OriginRecord!.Find(PackageId)!.Commit));
        foreach (var expected in new[] { PackageId, "4.0.0-preview", "4.0.1-preview", "the record's Elsa.Tasks entry was lost", "sfmskywalker" })
            Assert.Contains(expected, repaired.Message, StringComparison.Ordinal);

        var next = await RunAsync();

        Assert.True(next.Report!.Succeeded, next.Report.Failure);
        Assert.Equal(PackageOutcomeKind.Pushed, next.Report.Packages.Single(package => package.PackageId == PackageId).Kind);
        Assert.Equal("4.0.2-preview", RecordedVersions[PackageId]);
        Assert.Equal(["4.0.0-preview", "4.0.1-preview", "4.0.2-preview"], Feed.Versions(PackageId));
    }

    [Fact]
    public async Task Repair_refuses_a_commit_not_on_main()
    {
        var mainTip = (await BootstrapAsync()).Plan.Commit;
        Repo.Git("checkout", "--quiet", "-b", "side", Baseline);
        Edit("src/Http/Http.cs");
        var side = Repo.CommitAsIs("Side change");
        Repo.Git("checkout", "--quiet", "main");

        var refused = await Assert.ThrowsAsync<PublishRefusedException>(() => RepairMainCommitAsync(side, mainTip));

        Assert.Contains("not on main", refused.Message, StringComparison.Ordinal);
        Assert.Equal(mainTip, OriginRecord!.LastPublishCommit);
    }

    /// <summary>
    /// FR-021's edge case: after main is rewritten, publishing refuses until last_publish_commit names a commit of the
    /// new history; the repair resets it to one that does, naming the reason and who ran it, and publishing resumes.
    /// </summary>
    [Fact]
    public async Task A_repair_resets_last_publish_commit_after_a_rewritten_main_and_publishing_resumes()
    {
        var bootstrapCommit = (await BootstrapAsync()).Plan.Commit;
        Edit("src/Tasks/Scheduler.cs");
        var published = CommitAndPush();
        Assert.True((await RunAsync()).Report!.Succeeded);
        Assert.Equal(published, OriginRecord!.LastPublishCommit);

        Repo.Git("reset", "--quiet", "--hard", bootstrapCommit);
        Edit("src/Tasks/Scheduler.cs");
        Edit("src/Tasks/Scheduler.cs");
        var rewritten = Repo.CommitAsIs("Change Tasks, rewritten");

        var refused = await Assert.ThrowsAsync<VersionGateException>(() => RunAsync(commit: rewritten));
        Assert.Contains("FR-021", refused.Message, StringComparison.Ordinal);

        var repaired = await RepairMainCommitAsync(bootstrapCommit, rewritten, reason: "main was rewritten past #2121", actor: "sfmskywalker");

        Assert.Equal(bootstrapCommit, OriginRecord!.LastPublishCommit);
        foreach (var expected in new[] { published, bootstrapCommit, "main was rewritten past #2121", "sfmskywalker" })
            Assert.Contains(expected, repaired.Message, StringComparison.Ordinal);

        var next = await RunAsync(commit: rewritten);

        Assert.True(next.Report!.Succeeded, next.Report.Failure);
        Assert.Equal(rewritten, OriginRecord!.LastPublishCommit);
        Assert.Equal(PackageOutcomeKind.Pushed, next.Report.Packages.Single(package => package.PackageId == PackageId).Kind);
        Assert.Equal("4.0.2-preview", RecordedVersions[PackageId]);
    }

    /// <summary>Sets a package's record entry directly, bypassing the repair, to build the defect a repair settles.</summary>
    private void SetRecordEntry(string packageId, string version, string commit)
    {
        var tip = State.FetchTip()!;
        var record = State.Read(tip);
        var updated = new PublishedVersions(record.LastPublishCommit, record.Packages.Select(entry =>
            entry.PackageId == packageId ? entry with { Version = PackageVersionNumber.Parse(version, "test"), Commit = commit } : entry));
        State.Write(updated, tip, "Simulate a corrupted record entry");
    }

    private Task<RepairReport> RepairPackageAsync(string packageId, string reason = "settle a collision", string actor = "an operator", string reference = PublishPlan.MainRef) =>
        RepairCommand.RepairPackageAsync(new RepairPackageOptions(Repo.Root, reference, packageId, reason, actor, null), Feed, State, TextWriter.Null);

    private Task<RepairReport> RepairMainCommitAsync(
        string resetTo, string commit, string reason = "settle a rewritten main", string actor = "an operator", string reference = PublishPlan.MainRef) =>
        RepairCommand.RepairLastPublishCommitAsync(new RepairLastPublishCommitOptions(Repo.Root, reference, commit, resetTo, reason, actor, null), State, TextWriter.Null);
}
