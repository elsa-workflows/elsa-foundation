using Elsa.Versioning.Calculator;
using Elsa.Versioning.Publisher.Tests.Support;

namespace Elsa.Versioning.Publisher.Tests;

/// <summary>
/// Publishing from <c>main</c> once the record exists (#2082): only the affected set is pushed (FR-006), no push skips
/// a duplicate (FR-011), a rejected push is settled by fingerprint (FR-018), what landed is written back to
/// <c>publish-state</c> (FR-014, FR-017), and a publish only moves forward (FR-021). Every test starts from the bootstrap.
/// </summary>
public sealed class SelectivePublishTests : PublishingHistory, IAsyncLifetime
{
    private const string OtherFingerprint = "sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

    private string bootstrapCommit = null!;

    public async Task InitializeAsync() => bootstrapCommit = (await BootstrapAsync()).Plan.Commit;

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Spec 150 US1 scenario 1, SC-001, and #2082's acceptance: a commit touching one module pushes that module's package
    /// alone, one patch on; the record moves for it alone; and nothing on <c>main</c> changes (SC-012).
    /// </summary>
    [Fact]
    public async Task A_commit_touching_one_module_publishes_only_that_module()
    {
        Edit("src/Tasks/Scheduler.cs");
        var commit = CommitAndPush();
        var pushes = Feed.Pushes.Count;

        var run = await RunAsync();

        Assert.True(run.Report!.Succeeded, run.Report.Failure);
        Assert.Equal(["Elsa.Tasks 4.0.1-preview"], Feed.Taken.Skip(pushes));
        Assert.Equal(["4.0.0-preview", "4.0.1-preview"], Feed.Versions("Elsa.Tasks"));
        Assert.All(AllPackages.Where(id => id != "Elsa.Tasks"), id => Assert.Equal(["4.0.0-preview"], Feed.Versions(id)));

        var record = OriginRecord!;
        Assert.Equal(commit, record.LastPublishCommit);
        Assert.Equal(("4.0.1-preview", commit), (record.Find("Elsa.Tasks")!.Version.ToString(), record.Find("Elsa.Tasks")!.Commit));
        Assert.All(record.Packages.Where(entry => entry.PackageId != "Elsa.Tasks"), entry => Assert.Equal(("4.0.0-preview", bootstrapCommit), (entry.Version.ToString(), entry.Commit)));
        Assert.Equal((commit, commit), (GitProcess.Run(OriginPath, ["rev-parse", "main"]).Text, Repo.Head));
    }

    /// <summary>SC-009: a commit that changes no package pushes nothing and writes no record revision.</summary>
    [Fact]
    public async Task A_commit_that_changes_no_package_publishes_nothing()
    {
        Repo.Write("src/Tasks/README.md", "# Tasks, revised");
        CommitAndPush();
        var (pushes, tip) = (Feed.Pushes.Count, OriginStateTip);

        var run = await RunAsync();

        Assert.True(run.Report!.Succeeded);
        Assert.Empty(run.Report.Packages);
        Assert.Equal(pushes, Feed.Pushes.Count);
        Assert.Equal(tip, OriginStateTip);
    }

    /// <summary>
    /// #2082's acceptance: a run that dies between its pushes and its write-back is recovered by the next run with no
    /// intervention. The feed rejects the repeated push, the feed's copy carries the same input fingerprint, so the
    /// version is recorded as published and nothing is overwritten.
    /// </summary>
    [Fact]
    public async Task A_crash_between_push_and_write_back_is_recovered_by_the_next_run()
    {
        Edit("src/Tasks/Scheduler.cs");
        var commit = CommitAndPush();

        await Assert.ThrowsAsync<CrashException>(() => RunAsync(state: new CrashingBeforeWriteBack(State)));
        Assert.Equal("4.0.0-preview", RecordedVersions["Elsa.Tasks"]);
        var pushed = Feed.Package("Elsa.Tasks", "4.0.1-preview");
        Assert.NotNull(pushed);

        var run = await RunAsync();

        Assert.True(run.Report!.Succeeded, run.Report.Failure);
        Assert.Equal(PackageOutcomeKind.AlreadyPublished, run.Report.Packages.Single().Kind);
        Assert.Equal(("Elsa.Tasks", "4.0.1-preview", PushStatus.AlreadyExists), Feed.Pushes[^1]);
        Assert.Same(pushed, Feed.Package("Elsa.Tasks", "4.0.1-preview"));
        Assert.Equal("4.0.1-preview", RecordedVersions["Elsa.Tasks"]);
        Assert.Equal(commit, OriginRecord!.LastPublishCommit);
    }

    /// <summary>The next run need not be a re-run: a later commit that changed something else records the crashed push too.</summary>
    [Fact]
    public async Task A_crash_between_push_and_write_back_is_recovered_by_a_later_commit()
    {
        Edit("src/Tasks/Scheduler.cs");
        CommitAndPush();
        await Assert.ThrowsAsync<CrashException>(() => RunAsync(state: new CrashingBeforeWriteBack(State)));
        Edit("src/Http/Http.cs");
        var later = CommitAndPush();

        var run = await RunAsync();

        Assert.True(run.Report!.Succeeded, run.Report.Failure);
        Assert.Equal(
            [("Elsa.Http", PackageOutcomeKind.Pushed), ("Elsa.Tasks", PackageOutcomeKind.AlreadyPublished), ("dotnet-elsa", PackageOutcomeKind.Pushed)],
            run.Report.Packages.Select(package => (package.PackageId, package.Kind)));
        Assert.Equal("4.0.1-preview", RecordedVersions["Elsa.Tasks"]);
        Assert.Equal(later, OriginRecord!.Find("Elsa.Tasks")!.Commit);
    }

    /// <summary>
    /// FR-018's other direction: the feed holds the computed version built from other inputs — a lost record, a rewritten
    /// history. The publish fails naming the package, the version and both fingerprints, keeps the feed's copy, records
    /// nothing, and pushes nothing that depends on it.
    /// </summary>
    [Fact]
    public async Task A_version_on_the_feed_built_from_other_inputs_fails_the_publish_and_is_never_overwritten()
    {
        Feed.Seed("Elsa.Tasks", "4.0.1-preview", OtherFingerprint);
        var onFeed = Feed.Package("Elsa.Tasks", "4.0.1-preview");
        Edit("src/Tasks/Scheduler.cs");
        Edit("src/Tasks/Schedules/Schedule.cs");
        CommitAndPush();
        var tip = OriginStateTip;

        var run = await RunAsync();

        Assert.False(run.Report!.Succeeded);
        var collision = run.Report.Packages.Single(package => package.PackageId == "Elsa.Tasks");
        Assert.Equal(PackageOutcomeKind.Collision, collision.Kind);
        Assert.Contains(OtherFingerprint, collision.Detail, StringComparison.Ordinal);
        Assert.Contains(run.Computation["Elsa.Tasks"].Fingerprint, collision.Detail, StringComparison.Ordinal);
        Assert.Contains("Elsa.Tasks 4.0.1-preview", run.Report.Failure, StringComparison.Ordinal);
        Assert.Equal(PackageOutcomeKind.NotAttempted, run.Report.Packages.Single(package => package.PackageId == "Elsa.Tasks.Schedules").Kind);
        Assert.Same(onFeed, Feed.Package("Elsa.Tasks", "4.0.1-preview"));
        Assert.DoesNotContain(Feed.Pushes, push => push.PackageId == "Elsa.Tasks.Schedules" && push.Version == "4.0.1-preview");
        Assert.Null(run.Report.RecordCommit);
        Assert.Equal(tip, OriginStateTip);
    }

    /// <summary>A copy on the feed whose fingerprint cannot be read proves nothing, so it fails the publish the same way (FR-018).</summary>
    [Fact]
    public async Task A_version_on_the_feed_without_a_readable_fingerprint_fails_the_publish()
    {
        Feed.Seed("Elsa.Tasks", "4.0.1-preview", fingerprint: null);
        Edit("src/Tasks/Scheduler.cs");
        CommitAndPush();

        var run = await RunAsync();

        Assert.False(run.Report!.Succeeded);
        Assert.Equal(PackageOutcomeKind.Collision, run.Report.Packages.Single().Kind);
        Assert.Contains("cannot be read", run.Report.Failure, StringComparison.Ordinal);
        Assert.Equal("4.0.0-preview", RecordedVersions["Elsa.Tasks"]);
    }

    /// <summary>
    /// The partial-publish edge case: a failed push stops the publish before anything that depends on it, what did land
    /// is recorded, and the next run pushes the rest at the versions they failed at.
    /// </summary>
    [Fact]
    public async Task A_failed_push_stops_before_its_dependents_and_the_next_run_pushes_the_rest()
    {
        Edit("src/Tasks/Scheduler.cs");
        Edit("src/Tasks/Schedules/Schedule.cs");
        Edit("src/Http/Http.cs");
        CommitAndPush();
        Feed.FailingPushes.Add("Elsa.Tasks");

        var run = await RunAsync();

        Assert.False(run.Report!.Succeeded);
        Assert.Equal(
            [("Elsa.Http", PackageOutcomeKind.Pushed), ("Elsa.Tasks", PackageOutcomeKind.Failed), ("Elsa.Tasks.Schedules", PackageOutcomeKind.NotAttempted),
                ("dotnet-elsa", PackageOutcomeKind.NotAttempted)],
            run.Report.Packages.Select(package => (package.PackageId, package.Kind)));
        Assert.Equal(("4.0.1-preview", "4.0.0-preview"), (RecordedVersions["Elsa.Http"], RecordedVersions["Elsa.Tasks"]));

        Feed.FailingPushes.Clear();
        var next = await RunAsync();

        Assert.True(next.Report!.Succeeded, next.Report.Failure);
        Assert.Equal(["Elsa.Tasks", "Elsa.Tasks.Schedules", "dotnet-elsa"], next.Report.Packages.Select(package => package.PackageId));
        Assert.All(next.Report.Packages, package => Assert.Equal(PackageOutcomeKind.Pushed, package.Kind));
        Assert.Equal(("4.0.1-preview", "4.0.1-preview", "4.0.1-preview"),
            (RecordedVersions["Elsa.Tasks"], RecordedVersions["Elsa.Tasks.Schedules"], RecordedVersions["dotnet-elsa"]));
    }

    /// <summary>SC-014, FR-021: a run for a commit older than the record's latest publish refuses to plan, and so to push.</summary>
    [Fact]
    public async Task A_commit_older_than_the_latest_publish_is_refused()
    {
        Edit("src/Tasks/Scheduler.cs");
        var older = CommitAndPush();
        Edit("src/Http/Http.cs");
        CommitAndPush();
        await RunAsync();
        var pushes = Feed.Pushes.Count;

        var refused = await Assert.ThrowsAsync<VersionGateException>(() => RunAsync(commit: older));

        Assert.Contains("FR-021", refused.Message, StringComparison.Ordinal);
        Assert.Equal(pushes, Feed.Pushes.Count);
    }

    /// <summary>
    /// FR-020's reason: two runs computed against the same record would publish the same version twice. A run whose record
    /// moved while it waited — here, overtaken by a newer run — refuses before pushing anything.
    /// </summary>
    [Fact]
    public async Task A_run_whose_record_moved_since_it_was_planned_pushes_nothing()
    {
        Edit("src/Tasks/Scheduler.cs");
        CommitAndPush();
        var overtaken = PlanAndPack();
        Edit("src/Http/Http.cs");
        CommitAndPush();
        await RunAsync();
        var (pushes, tip) = (Feed.Pushes.Count, OriginStateTip);

        var refused = await Assert.ThrowsAsync<PublishRefusedException>(() => PublishAsync(overtaken));

        Assert.Contains("moved", refused.Message, StringComparison.Ordinal);
        Assert.Equal(pushes, Feed.Pushes.Count);
        Assert.Equal(tip, OriginStateTip);
    }

    /// <summary>
    /// A write-back rejected because <c>publish-state</c> moved under it — it is never forced — fails the run, and the pushed
    /// packages are recorded by the next run through FR-018.
    /// </summary>
    [Fact]
    public async Task A_rejected_write_back_fails_the_run_and_the_next_run_records_the_packages()
    {
        Edit("src/Tasks/Scheduler.cs");
        CommitAndPush();

        var run = await RunAsync(state: new MovedBeforeWriteBack(State, OriginPath));

        Assert.False(run.Report!.Succeeded);
        Assert.Equal(PackageOutcomeKind.Pushed, run.Report.Packages.Single().Kind);
        Assert.Contains("write-back failed", run.Report.Failure, StringComparison.Ordinal);
        Assert.Equal("4.0.0-preview", RecordedVersions["Elsa.Tasks"]);

        var next = await RunAsync();

        Assert.True(next.Report!.Succeeded, next.Report.Failure);
        Assert.Equal(PackageOutcomeKind.AlreadyPublished, next.Report.Packages.Single().Kind);
        Assert.Equal("4.0.1-preview", RecordedVersions["Elsa.Tasks"]);
    }

    /// <summary>
    /// The packages must be exactly what was computed: one packed from other inputs, or one the computation does not
    /// publish, refuses the whole publish before any push.
    /// </summary>
    [Fact]
    public async Task Packages_that_are_not_the_computed_affected_set_refuse_the_publish_before_any_push()
    {
        Edit("src/Tasks/Scheduler.cs");
        CommitAndPush();
        var run = PlanAndPack();
        var packages = Path.Join(run.Directory, PlanFiles.Packages);
        File.WriteAllBytes(Path.Join(packages, "Elsa.Tasks.4.0.1-preview.nupkg"), SyntheticPackages.Bytes("Elsa.Tasks", "4.0.1-preview", OtherFingerprint, run.Plan.Commit));
        File.WriteAllBytes(Path.Join(packages, "Elsa.Http.4.0.0-preview.nupkg"), SyntheticPackages.Bytes("Elsa.Http", "4.0.0-preview", OtherFingerprint, run.Plan.Commit));
        var pushes = Feed.Pushes.Count;

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => PublishAsync(run));

        Assert.Contains($"Elsa.Tasks 4.0.1-preview carries input fingerprint {OtherFingerprint}", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Elsa.Http was packed, but the computation does not publish it", refused.Message, StringComparison.Ordinal);
        Assert.Equal(pushes, Feed.Pushes.Count);
    }

    /// <summary>FR-003's force-advance at publish time: the plan carries it, and the publish job recomputes with it.</summary>
    [Fact]
    public async Task A_force_advance_publishes_the_named_package()
    {
        var run = await RunAsync(forced: new ForcedAdvance(["Elsa.Http"], "republish with a patched floor"));

        Assert.True(run.Report!.Succeeded, run.Report.Failure);
        Assert.Equal(["Elsa.Http", "dotnet-elsa"], run.Report.Packages.Select(package => package.PackageId));
        Assert.Equal("4.0.1-preview", RecordedVersions["Elsa.Http"]);
    }

    private sealed class CrashException() : Exception("The runner died.");

    /// <summary>A run that dies after its pushes and before its write-back.</summary>
    private sealed class CrashingBeforeWriteBack(IPublishState inner) : IPublishState
    {
        public string? FetchTip() => inner.FetchTip();

        public IReadOnlyList<string> Revisions(string tip) => inner.Revisions(tip);

        public PublishedVersions Read(string revision) => inner.Read(revision);

        public string Write(PublishedVersions record, string? parent, string message) => throw new CrashException();
    }

    /// <summary>Something else writes <c>publish-state</c> between a run's pushes and its write-back.</summary>
    private sealed class MovedBeforeWriteBack(IPublishState inner, string origin) : IPublishState
    {
        public string? FetchTip() => inner.FetchTip();

        public IReadOnlyList<string> Revisions(string tip) => inner.Revisions(tip);

        public PublishedVersions Read(string revision) => inner.Read(revision);

        public string Write(PublishedVersions record, string? parent, string message)
        {
            var tree = GitProcess.Run(origin, ["rev-parse", $"{parent}^{{tree}}"]).Text;
            var moved = GitProcess.Run(origin, ["commit-tree", tree, "-p", parent!, "--no-gpg-sign", "-m", "Another writer"],
                environment: new Dictionary<string, string> { ["GIT_AUTHOR_NAME"] = "a", ["GIT_AUTHOR_EMAIL"] = "a@example.invalid", ["GIT_COMMITTER_NAME"] = "a", ["GIT_COMMITTER_EMAIL"] = "a@example.invalid" }).Text;
            GitProcess.Run(origin, ["update-ref", $"refs/heads/{GitPublishState.DefaultBranch}", moved]);
            return inner.Write(record, parent, message);
        }
    }
}
