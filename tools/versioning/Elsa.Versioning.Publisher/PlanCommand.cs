using System.Text.Json;
using Elsa.Versioning.Calculator;

namespace Elsa.Versioning.Publisher;

/// <summary>The files a plan writes into its output directory, which the pack job builds from and hands to the publish job.</summary>
public static class PlanFiles
{
    public const string Plan = "plan.json";
    public const string Computation = "computation.json";
    public const string PackProperties = "package-versions.props";
    public const string PackFilter = "packages.slnf";
    public const string Packages = "nuget";
}

/// <param name="Repository">A directory inside the repository.</param>
/// <param name="Ref">The ref being built, as <c>GITHUB_REF</c> gives it.</param>
/// <param name="Commit">The commit being built.</param>
/// <param name="Bootstrap">True for the manually dispatched bootstrap.</param>
/// <param name="Forced">A force-advance to compute with (spec 150 FR-003), or null.</param>
/// <param name="OutputDirectory">Where the <see cref="PlanFiles"/> go.</param>
/// <param name="Solution">The solution the pack filter selects projects from.</param>
/// <param name="GitHubOutput">The step's <c>GITHUB_OUTPUT</c> file, to which <c>mode</c> and <c>affected</c> are appended, or null.</param>
/// <param name="Summary">The step's <c>GITHUB_STEP_SUMMARY</c> file, or null.</param>
public sealed record PlanOptions(
    string Repository, string Ref, string Commit, bool Bootstrap, ForcedAdvance? Forced, string OutputDirectory, string Solution, string? GitHubOutput, string? Summary);

/// <summary>
/// The pack job's first step: decides the run's mode, computes every package's version against the record the mode
/// calls for, and writes what packing needs — the calculator's output, its pack-properties file for the branch being
/// built, and a solution filter holding exactly the affected set (spec 150 FR-006a).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A publish computes against <c>publish-state</c>'s tip, which the publish job requires to be unchanged.</item>
/// <item>The bootstrap, and a dry run on <c>main</c> before it, compute against an empty record: every package's first
/// version.</item>
/// <item>A branch build computes against the newest revision of the record whose latest publish its commit descends from,
/// so a branch cut before a publish still builds (FR-021 holds for it as for main), and against an empty record when
/// none is; it never pushes.</item>
/// </list>
/// </remarks>
public static class PlanCommand
{
    private static readonly JsonSerializerOptions FilterJson = new() { WriteIndented = true, IndentSize = 2, NewLine = "\n" };

    public static (PublishPlan Plan, VersionComputation Computation) Run(PlanOptions options, IPublishState state)
    {
        var git = new GitRepository(options.Repository);
        var commit = git.ResolveCommit(options.Commit);
        var tip = state.FetchTip();
        var mode = PublishPlan.Decide(options.Ref, options.Bootstrap, tip is not null);
        var stateCommit = mode switch
        {
            PublishMode.Publish => tip,
            PublishMode.Branch when tip is not null => state.Revisions(tip).FirstOrDefault(revision => Descends(git, commit, state.Read(revision))),
            _ => null
        };

        var plan = new PublishPlan(mode, options.Ref, commit, stateCommit, options.Forced);
        var computation = Compute(git, plan, RecordFor(plan, state));
        var packProperties = PackProperties.Render(computation, plan.Branch);

        Directory.CreateDirectory(options.OutputDirectory);
        File.WriteAllText(Path.Join(options.OutputDirectory, PlanFiles.Plan), plan.Serialize());
        File.WriteAllText(Path.Join(options.OutputDirectory, PlanFiles.Computation), computation.ToJson());
        File.WriteAllText(Path.Join(options.OutputDirectory, PlanFiles.PackProperties), packProperties);
        File.WriteAllText(Path.Join(options.OutputDirectory, PlanFiles.PackFilter), PackFilter(computation, options.Solution, options.OutputDirectory));

        if (options.GitHubOutput is { } output)
            File.AppendAllText(output, $"mode={mode.Name()}\naffected={computation.Affected.Count}\n");
        if (options.Summary is { } summary)
            File.AppendAllText(summary, JobSummary.Plan(plan, computation, PackProperties.LabelFor(computation, plan.Branch)));

        return (plan, computation);
    }

    /// <summary>The record a plan computes against: the one at its state commit, or an empty one when it names none.</summary>
    public static PublishedVersions RecordFor(PublishPlan plan, IPublishState state) =>
        plan.StateCommit is { } revision ? state.Read(revision) : new PublishedVersions(null, []);

    /// <summary>The computation a plan stands for. The publish job recomputes it to check that nothing moved since the pack job.</summary>
    public static VersionComputation Compute(GitRepository git, PublishPlan plan, PublishedVersions record) =>
        VersionCalculator.Compute(git, plan.Commit, record, plan.Forced);

    private static bool Descends(GitRepository git, string commit, PublishedVersions record) =>
        record.LastPublishCommit is not { } latest || (git.HasCommit(latest) && git.IsAncestorOrSelf(latest, commit));

    /// <summary>A solution filter selecting the affected set, its solution path relative to the filter as MSBuild reads it.</summary>
    private static string PackFilter(VersionComputation computation, string solution, string outputDirectory) =>
        JsonSerializer.Serialize(
            new
            {
                solution = new
                {
                    path = Path.GetRelativePath(Path.GetFullPath(outputDirectory), Path.GetFullPath(solution)).Replace('\\', '/'),
                    projects = computation.Packages.Where(package => package.Affected).Select(package => package.Path).Order(StringComparer.Ordinal).ToArray()
                }
            },
            FilterJson) + "\n";
}
