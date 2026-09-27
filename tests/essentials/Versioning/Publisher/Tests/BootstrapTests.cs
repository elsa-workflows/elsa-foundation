using Elsa.Versioning.Calculator;
using Elsa.Versioning.Publisher.Tests.Support;

namespace Elsa.Versioning.Publisher.Tests;

/// <summary>
/// Before the record exists (#2081): a push to <c>main</c> only packs and reports, and the one-off bootstrap, dispatched by
/// hand, publishes every package and creates <c>publish-state</c> — but only once the feed holds nothing its versions
/// would not sort above, and never a second time.
/// </summary>
public sealed class BootstrapTests : PublishingHistory
{
    /// <summary>The owner's gate: until the bootstrap runs, main packs everything at its bootstrap version and pushes nothing.</summary>
    [Fact]
    public async Task A_push_to_main_without_the_record_is_a_dry_run_that_publishes_nothing()
    {
        var run = await RunAsync();

        Assert.Equal(PublishMode.DryRun, run.Plan.Mode);
        Assert.Equal(AllPackages, run.Computation.Affected);
        Assert.All(run.Computation.Packages, package => Assert.Equal("4.0.0", package.Version.Numeric));
        Assert.Equal(AllPackages.Length, Directory.EnumerateFiles(Path.Join(run.Directory, PlanFiles.Packages), "*.nupkg").Count());
        Assert.Contains("Bootstrap pending", File.ReadAllText(Path.Join(run.Directory, "summary.md")), StringComparison.Ordinal);
        Assert.Equal($"mode=dry-run\naffected={AllPackages.Length}\n", File.ReadAllText(Path.Join(run.Directory, "github-output")));

        var refused = await Assert.ThrowsAsync<PublishRefusedException>(() => PublishAsync(run));
        Assert.Contains("dry-run", refused.Message, StringComparison.Ordinal);
        Assert.Empty(Feed.Pushes);
        Assert.Null(OriginStateTip);
    }

    /// <summary>
    /// #2081's acceptance: <c>publish-state</c> exists as an orphan branch holding only <c>published-versions.json</c>, with an
    /// entry for every packable package id at its computed version, naming the main commit it was built from (FR-014, FR-021).
    /// </summary>
    [Fact]
    public async Task The_bootstrap_publishes_every_package_and_creates_publish_state_as_an_orphan_holding_only_the_record()
    {
        var run = await BootstrapAsync();

        Assert.Equal(AllPackages.Select(id => $"{id} 4.0.0-preview").Order(StringComparer.Ordinal), Feed.Taken.Order(StringComparer.Ordinal));
        Assert.Equal(run.Report!.RecordCommit, OriginStateTip);
        Assert.Equal("1", GitProcess.Run(OriginPath, ["rev-list", "--count", GitPublishState.DefaultBranch]).Text);
        Assert.Equal(OriginStateTip, GitProcess.Run(OriginPath, ["rev-list", "--parents", GitPublishState.DefaultBranch]).Text);
        Assert.Equal(PublishedVersions.DefaultPath, GitProcess.Run(OriginPath, ["ls-tree", "-r", "--name-only", GitPublishState.DefaultBranch]).Text);

        var record = OriginRecord!;
        Assert.Equal(Repo.Head, record.LastPublishCommit);
        Assert.Equal(AllPackages.Order(StringComparer.Ordinal), record.Packages.Select(entry => entry.PackageId));
        Assert.All(record.Packages, entry => Assert.Equal(("4.0.0-preview", Repo.Head), (entry.Version.ToString(), entry.Commit)));
        Assert.Equal(Repo.Head, GitProcess.Run(OriginPath, ["rev-parse", "main"]).Text);
    }

    /// <summary>
    /// The owner's precondition: the old <c>4.0.0-preview.N</c> and branch builds sort above <c>4.0.0-preview</c> or beside it,
    /// so while any remains the bootstrap names them and pushes nothing. An older major is no obstacle.
    /// </summary>
    [Fact]
    public async Task The_bootstrap_refuses_while_old_previews_remain_on_the_feed_naming_them()
    {
        Feed.Seed("Elsa.Tasks", "4.0.0-preview.977");
        Feed.Seed("Elsa.Tasks", "4.0.0-preview.12");
        Feed.Seed("Elsa.Http", "4.0.0-issue-2080.45");
        Feed.Seed("Elsa.Http", "3.5.0");

        var refused = await Assert.ThrowsAsync<PublishRefusedException>(() => RunAsync(bootstrap: true));

        Assert.Contains("Elsa.Tasks: 4.0.0-preview.12, 4.0.0-preview.977", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Elsa.Http: 4.0.0-issue-2080.45", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("3.5.0", refused.Message, StringComparison.Ordinal);
        Assert.Empty(Feed.Pushes);
        Assert.Null(OriginStateTip);
    }

    /// <summary>A feed that cannot say what it holds proves nothing about old previews, so the bootstrap refuses.</summary>
    [Fact]
    public async Task The_bootstrap_refuses_when_the_feed_cannot_list_what_it_holds()
    {
        Feed.ListingFails = true;

        var refused = await Assert.ThrowsAsync<PublishRefusedException>(() => RunAsync(bootstrap: true));

        Assert.Contains("could not list", refused.Message, StringComparison.Ordinal);
        Assert.Empty(Feed.Pushes);
        Assert.Null(OriginStateTip);
    }

    /// <summary>
    /// The bootstrap is one-off: once <c>publish-state</c> exists the plan refuses it, and a bootstrap planned before the
    /// branch existed refuses at publish time, before pushing anything.
    /// </summary>
    [Fact]
    public async Task The_bootstrap_refuses_once_the_record_exists()
    {
        var late = PlanAndPack(bootstrap: true);
        await BootstrapAsync();
        var pushes = Feed.Pushes.Count;
        var tip = OriginStateTip;

        Assert.Contains("already exists", Assert.Throws<PublishRefusedException>(() => PlanAndPack(bootstrap: true)).Message, StringComparison.Ordinal);
        Assert.Contains("one-off", (await Assert.ThrowsAsync<PublishRefusedException>(() => PublishAsync(late))).Message, StringComparison.Ordinal);
        Assert.Equal(pushes, Feed.Pushes.Count);
        Assert.Equal(tip, OriginStateTip);
    }

    /// <summary>
    /// A bootstrap that stops part way records what it pushed, so <c>publish-state</c> exists and the next push to main
    /// publishes the rest at their first versions: no second bootstrap, and nothing to delete from the feed.
    /// </summary>
    [Fact]
    public async Task A_bootstrap_that_stops_part_way_records_what_it_pushed_and_the_next_run_completes_it()
    {
        Feed.FailingPushes.Add("Elsa.Tasks");

        var run = await RunAsync(bootstrap: true);

        Assert.False(run.Report!.Succeeded);
        Assert.Equal(PackageOutcomeKind.Failed, run.Report.Packages.Single(package => package.PackageId == "Elsa.Tasks").Kind);
        Assert.Equal(PackageOutcomeKind.NotAttempted, run.Report.Packages.Single(package => package.PackageId == "Elsa.Tasks.Schedules").Kind);
        var recorded = run.Report.Packages.Where(package => package.Recorded).Select(package => package.PackageId).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(recorded, OriginRecord!.Packages.Select(entry => entry.PackageId));
        Assert.DoesNotContain("Elsa.Tasks", recorded);

        Feed.FailingPushes.Clear();
        var next = await RunAsync();

        Assert.Equal(PublishMode.Publish, next.Plan.Mode);
        Assert.True(next.Report!.Succeeded, next.Report.Failure);
        Assert.Equal(AllPackages.Except(recorded).Order(StringComparer.Ordinal), next.Computation.Affected);
        Assert.All(RecordedVersions.Values, version => Assert.Equal("4.0.0-preview", version));
        Assert.Equal(AllPackages.Order(StringComparer.Ordinal), RecordedVersions.Keys.Order(StringComparer.Ordinal));
    }
}
