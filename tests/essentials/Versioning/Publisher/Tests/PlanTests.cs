using System.Text.Json;
using Elsa.Versioning.Calculator;
using Elsa.Versioning.Publisher.Tests.Support;

namespace Elsa.Versioning.Publisher.Tests;

/// <summary>
/// The pack job's plan: which mode a run takes, what it computes against, and what it hands packing and the publish
/// job. Branch builds pack for CI artifacts and can never push (spec 150 FR-008).
/// </summary>
public sealed class PlanTests : PublishingHistory
{
    public static TheoryData<string, bool, bool, PublishMode> Modes => new()
    {
        { "refs/heads/main", false, false, PublishMode.DryRun },
        { "refs/heads/main", true, false, PublishMode.Bootstrap },
        { "refs/heads/main", false, true, PublishMode.Publish },
        { "refs/heads/feat/issue-2082", false, false, PublishMode.Branch },
        { "refs/heads/feat/issue-2082", false, true, PublishMode.Branch },
        { "refs/heads/release/4.0", false, true, PublishMode.Branch }
    };

    [Theory]
    [MemberData(nameof(Modes))]
    public void The_mode_follows_the_branch_and_whether_the_record_exists(string reference, bool bootstrap, bool stateExists, PublishMode expected) =>
        Assert.Equal(expected, PublishPlan.Decide(reference, bootstrap, stateExists));

    /// <summary>The bootstrap runs from main, once; a ref that names no branch packs nothing.</summary>
    [Theory]
    [InlineData("refs/heads/main", true, true, typeof(PublishRefusedException))]
    [InlineData("refs/heads/feat/issue-2082", true, false, typeof(PublishRefusedException))]
    [InlineData("refs/tags/4.0.0", false, false, typeof(ArgumentException))]
    [InlineData("refs/pull/2082/merge", false, true, typeof(ArgumentException))]
    [InlineData("main", false, true, typeof(ArgumentException))]
    public void A_bootstrap_off_main_or_after_the_record_and_a_ref_that_is_no_branch_are_refused(string reference, bool bootstrap, bool stateExists, Type expected) =>
        Assert.IsType(expected, Xunit.Record.Exception(() => PublishPlan.Decide(reference, bootstrap, stateExists)));

    /// <summary>
    /// US4 scenario 2: a branch build carries its branch's label and packs only what changed, and nothing it plans can be
    /// pushed — not its own plan, and not a publish plan run on the branch.
    /// </summary>
    [Fact]
    public async Task A_branch_build_packs_at_its_own_label_and_can_never_push()
    {
        await BootstrapAsync();
        Repo.Git("checkout", "--quiet", "-b", "feat/issue-2082");
        Edit("src/Tasks/Scheduler.cs");
        Repo.Commit();
        var pushes = Feed.Pushes.Count;

        var run = await RunAsync(reference: "refs/heads/feat/issue-2082");

        Assert.Equal(PublishMode.Branch, run.Plan.Mode);
        Assert.Equal(["Elsa.Tasks"], run.Computation.Affected);
        Assert.True(File.Exists(Path.Join(run.Directory, PlanFiles.Packages, "Elsa.Tasks.4.0.1-branch-feat-issue-2082.nupkg")));
        Assert.Contains("<ElsaComputedPackageVersion>4.0.1-branch-feat-issue-2082</ElsaComputedPackageVersion>",
            File.ReadAllText(Path.Join(run.Directory, PlanFiles.PackProperties)), StringComparison.Ordinal);
        Assert.Null(run.Report);
        await Assert.ThrowsAsync<PublishRefusedException>(() => PublishAsync(run));

        var onMain = PlanAndPack(commit: Repo.Head);
        await Assert.ThrowsAsync<PublishRefusedException>(() => PublishAsync(onMain, reference: "refs/heads/feat/issue-2082"));
        Assert.Equal(pushes, Feed.Pushes.Count);
    }

    /// <summary>
    /// A branch cut before main's latest publish still builds: it computes against the newest record it descends from,
    /// so the forward-only gate holds for it and it packs only its own change.
    /// </summary>
    [Fact]
    public async Task A_branch_cut_before_the_latest_publish_builds_against_the_record_it_descends_from()
    {
        var bootstrapState = (await BootstrapAsync()).Report!.RecordCommit;
        Repo.Git("checkout", "--quiet", "-b", "feat/old");
        Edit("src/Http/Http.cs");
        Repo.Commit("Branch change");
        Repo.Git("checkout", "--quiet", "main");
        Edit("src/Tasks/Scheduler.cs");
        CommitAndPush();
        await RunAsync();
        Repo.Git("checkout", "--quiet", "feat/old");

        var run = PlanAndPack(reference: "refs/heads/feat/old");

        Assert.Equal(bootstrapState, run.Plan.StateCommit);
        Assert.Equal(["Elsa.Http", "dotnet-elsa"], run.Computation.Affected);
    }

    /// <summary>What the pack job reads: the mode and count for the workflow, and a pack filter of exactly the affected set.</summary>
    [Fact]
    public async Task The_plan_hands_the_workflow_its_mode_and_packing_exactly_the_affected_set()
    {
        await BootstrapAsync();
        Edit("src/Tasks/Scheduler.cs");
        CommitAndPush();

        var run = PlanAndPack(forced: new ForcedAdvance(["Elsa.Http"], "a reason"));

        Assert.Equal("mode=publish\naffected=3\n", File.ReadAllText(Path.Join(run.Directory, "github-output")));
        using var filter = JsonDocument.Parse(File.ReadAllText(Path.Join(run.Directory, PlanFiles.PackFilter)));
        Assert.Equal(
            ["src/Cli/Elsa.Cli.csproj", "src/Http/Elsa.Http.csproj", "src/Tasks/Elsa.Tasks.csproj"],
            filter.RootElement.GetProperty("solution").GetProperty("projects").EnumerateArray().Select(project => project.GetString()));
        Assert.Equal(Path.Join(Repo.Root, "Elsa.Server.slnx"),
            Path.GetFullPath(Path.Join(run.Directory, filter.RootElement.GetProperty("solution").GetProperty("path").GetString())));
        Assert.Equal(run.Computation.ToJson(), File.ReadAllText(Path.Join(run.Directory, PlanFiles.Computation)));
        Assert.Equal(run.Plan.Serialize(), PublishPlan.Load(Path.Join(run.Directory, PlanFiles.Plan)).Serialize());
        Assert.Equal(("Elsa.Http", "a reason"), (PublishPlan.Load(Path.Join(run.Directory, PlanFiles.Plan)).Forced!.PackageIds.Single(), run.Plan.Forced!.Reason));
    }

    /// <summary>
    /// The direction that would look like success: a remote that cannot be asked about <c>publish-state</c> must not be
    /// taken for one without it, or main would quietly fall back to a dry run and stop publishing.
    /// </summary>
    [Fact]
    public void A_remote_that_cannot_be_asked_is_never_taken_for_one_without_the_record()
    {
        Repo.Git("remote", "set-url", GitPublishState.DefaultRemote, Path.Join(Repo.Root, ".git", "no-such-remote.git"));

        Assert.Throws<InvalidOperationException>(() => PlanAndPack());
    }
}
