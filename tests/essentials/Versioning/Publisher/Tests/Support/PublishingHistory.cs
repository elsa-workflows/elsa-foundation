using Elsa.Versioning.Calculator;
using Elsa.Versioning.Calculator.Tests.Support;

namespace Elsa.Versioning.Publisher.Tests.Support;

/// <summary>A workflow run: its plan and computation, and the publish job's report when the plan pushes.</summary>
public sealed record WorkflowRun(PublishPlan Plan, VersionComputation Computation, string Directory, PublishReport? Report);

/// <summary>
/// The calculator's synthetic history with a remote: a bare repository as <c>origin</c>, which <c>main</c> is pushed to and
/// <c>publish-state</c> lives on, and a <see cref="FakeFeed"/>. Every run goes through the same plan, pack and publish
/// steps the Packages workflow runs, with <see cref="SyntheticPackages"/> packing.
/// </summary>
/// <remarks>
/// Main's prerelease label is assigned on top of the baseline, as it is in this repository, and no record exists until
/// a test bootstraps one.
/// </remarks>
public abstract class PublishingHistory : SyntheticHistory
{
    private int runs;

    protected PublishingHistory()
    {
        Repo.PrereleaseLabel = "preview";
        Repo.Commit("Label main's packages");

        // Inside .git: removed with the repository, and never part of a commit.
        Repo.Git("init", "--quiet", "--bare", "--initial-branch=main", OriginPath);
        Repo.Git("remote", "add", GitPublishState.DefaultRemote, OriginPath);
        PushMain();
        State = new GitPublishState(Repo.Root);
    }

    private protected FakeFeed Feed { get; } = new();

    /// <summary>The record branch on <c>origin</c>, as the workflow reaches it.</summary>
    protected GitPublishState State { get; }

    protected string OriginPath => Path.Join(Repo.Root, ".git", "origin.git");

    /// <summary>Commits the working tree and pushes <c>main</c>.</summary>
    protected string CommitAndPush(string message = "change")
    {
        var commit = Repo.Commit(message);
        PushMain();
        return commit;
    }

    /// <summary>The bootstrap, which every publish test starts from: every package at <c>4.0.0-preview</c>.</summary>
    protected async Task<WorkflowRun> BootstrapAsync()
    {
        var run = await RunAsync(bootstrap: true);
        Assert.True(run.Report!.Succeeded, run.Report.Failure);
        return run;
    }

    /// <summary>One workflow run: the pack job's plan and pack, then the publish job when the plan pushes.</summary>
    protected async Task<WorkflowRun> RunAsync(
        bool bootstrap = false, string reference = PublishPlan.MainRef, string? commit = null, ForcedAdvance? forced = null, IPublishState? state = null)
    {
        var run = PlanAndPack(bootstrap, reference, commit, forced);
        return run.Plan.Mode is PublishMode.Publish or PublishMode.Bootstrap ? run with { Report = await PublishAsync(run, state) } : run;
    }

    /// <summary>The pack job: plans into a fresh directory and packs what the plan publishes.</summary>
    protected WorkflowRun PlanAndPack(bool bootstrap = false, string reference = PublishPlan.MainRef, string? commit = null, ForcedAdvance? forced = null)
    {
        var directory = Path.Join(Repo.Root, ".git", "runs", (++runs).ToString(System.Globalization.CultureInfo.InvariantCulture));
        var (plan, computation) = PlanCommand.Run(
            new PlanOptions(Repo.Root, reference, commit ?? Repo.Head, bootstrap, forced, directory, Path.Join(Repo.Root, "Elsa.Server.slnx"),
                Path.Join(directory, "github-output"), Path.Join(directory, "summary.md")),
            State);
        SyntheticPackages.Pack(Path.Join(directory, PlanFiles.Packages), computation, plan.Branch, Repo);
        return new WorkflowRun(plan, computation, directory, null);
    }

    /// <summary>The publish job, for a run's plan directory.</summary>
    protected Task<PublishReport> PublishAsync(WorkflowRun run, IPublishState? state = null, string reference = PublishPlan.MainRef) =>
        PublishCommand.RunAsync(new PublishOptions(Repo.Root, reference, run.Plan.Commit, run.Directory, Path.Join(run.Directory, "summary.md")),
            Feed, state ?? State, TextWriter.Null);

    /// <summary><c>publish-state</c>'s commit on <c>origin</c>, or null when the branch does not exist.</summary>
    protected string? OriginStateTip =>
        GitProcess.Run(OriginPath, ["rev-parse", "--verify", "--quiet", $"refs/heads/{GitPublishState.DefaultBranch}"]) is { ExitCode: 0 } result ? result.Text : null;

    /// <summary>The record on <c>origin</c>, or null when <c>publish-state</c> does not exist.</summary>
    protected PublishedVersions? OriginRecord =>
        OriginStateTip is { } tip ? PublishedVersions.Load(new GitRepository(OriginPath), tip) : null;

    /// <summary>The recorded version of each package on <c>origin</c>, by package id.</summary>
    protected IReadOnlyDictionary<string, string> RecordedVersions =>
        OriginRecord!.Packages.ToDictionary(entry => entry.PackageId, entry => entry.Version.ToString(), StringComparer.Ordinal);

    private void PushMain() => Repo.Git("push", "--quiet", GitPublishState.DefaultRemote, "main");
}
