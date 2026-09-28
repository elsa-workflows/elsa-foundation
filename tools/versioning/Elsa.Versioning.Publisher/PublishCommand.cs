using Elsa.Versioning.Calculator;

namespace Elsa.Versioning.Publisher;

/// <param name="Repository">A directory inside the repository.</param>
/// <param name="Ref">The ref being built, as <c>GITHUB_REF</c> gives it.</param>
/// <param name="Commit">The commit being built.</param>
/// <param name="PlanDirectory">The pack job's plan output, with the packed packages in its <see cref="PlanFiles.Packages"/> directory.</param>
/// <param name="Summary">The step's <c>GITHUB_STEP_SUMMARY</c> file, or null.</param>
public sealed record PublishOptions(string Repository, string Ref, string Commit, string PlanDirectory, string? Summary);

/// <summary>
/// The publish job: re-establishes, right before pushing, everything the pack job's plan assumed, then hands the
/// packages to <see cref="PackagePublisher"/>.
/// </summary>
/// <remarks>
/// It pushes only for a plan of <c>main</c> in the publish or bootstrap mode (spec 150 FR-008). It asks the remote for
/// <c>publish-state</c> again: a publish refuses when the branch moved since the versions were computed, and the
/// bootstrap when the branch exists. It then recomputes the versions and requires them to be the bytes the pack job
/// computed, so what it pushes and records is exactly what was packed, against the record it writes onto. The
/// forward-only gate (FR-021) runs again in that recomputation.
/// </remarks>
public static class PublishCommand
{
    public static async Task<PublishReport> RunAsync(PublishOptions options, IPackageFeed feed, IPublishState state, TextWriter log, CancellationToken cancellationToken = default)
    {
        var plan = PublishPlan.Load(Path.Join(options.PlanDirectory, PlanFiles.Plan));
        if (plan.Mode is not (PublishMode.Publish or PublishMode.Bootstrap))
            throw new PublishRefusedException($"The plan is a {plan.Mode.Name()} run, which pushes nothing; only a publish or the bootstrap does.");
        if (options.Ref != PublishPlan.MainRef || plan.Ref != PublishPlan.MainRef)
            throw new PublishRefusedException($"Only a build of main pushes (spec 150 FR-008); this is {options.Ref}, planned for {plan.Ref}.");

        var git = new GitRepository(options.Repository);
        var commit = git.ResolveCommit(options.Commit);
        if (commit != plan.Commit)
            throw new InvalidOperationException($"The plan was made for {plan.Commit}, but this is {commit}.");

        var tip = state.FetchTip();
        if (plan.Mode == PublishMode.Bootstrap && tip is not null)
            throw new PublishRefusedException(
                $"{GitPublishState.DefaultBranch} exists at {tip}, so the record was bootstrapped already. The bootstrap is one-off (spec 150 FR-014); nothing was pushed.");
        if (plan.Mode == PublishMode.Publish && tip != plan.StateCommit)
            throw new PublishRefusedException(
                $"{GitPublishState.DefaultBranch} moved from {plan.StateCommit} to {tip ?? "nothing"} since the versions were computed, so they may not be the ones " +
                "to publish. Nothing was pushed; run the workflow from main again.");

        var record = PlanCommand.RecordFor(plan, state);
        var computation = PlanCommand.Compute(git, plan, record);
        if (computation.ToJson() != await File.ReadAllTextAsync(Path.Join(options.PlanDirectory, PlanFiles.Computation), cancellationToken))
            throw new InvalidOperationException(
                $"Recomputing {commit} against {GitPublishState.DefaultBranch} {tip ?? "(none)"} gives other versions than the pack job's {PlanFiles.Computation}. Nothing was pushed.");

        var report = await new PackagePublisher(feed, state, log).PublishAsync(
            plan, computation, record, Path.Join(options.PlanDirectory, PlanFiles.Packages), cancellationToken);

        if (options.Summary is { } summary)
            await File.AppendAllTextAsync(summary, JobSummary.Publish(report), cancellationToken);

        return report;
    }
}
