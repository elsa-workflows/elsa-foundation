using System.Text;
using Elsa.Versioning.Calculator;

namespace Elsa.Versioning.Publisher;

/// <summary>The Markdown each step appends to the workflow run's job summary.</summary>
public static class JobSummary
{
    private const int ReasonsShown = 3;

    /// <summary>What the pack job decided, and the packages it packs.</summary>
    public static string Plan(PublishPlan plan, VersionComputation computation, string? label)
    {
        var text = new StringBuilder().Append($"### Packages: {plan.Mode.Name()}\n\n").Append(plan.Mode switch
        {
            PublishMode.Branch =>
                "A branch build: packed for CI artifacts at branch-scoped versions. Nothing is pushed to any feed (spec 150 FR-008).",
            PublishMode.DryRun =>
                $"**Bootstrap pending.** `{GitPublishState.DefaultBranch}` does not exist, so this run packed every package at the version the " +
                "bootstrap would publish, as a dry run, and **published nothing**. The first publish is the bootstrap: once no `4.0.0-*` prerelease " +
                $"remains on the feed, run the Packages workflow from `main` with `bootstrap: true`.",
            PublishMode.Publish =>
                $"Publishing the affected set: each package below is pushed, then recorded on `{GitPublishState.DefaultBranch}`.",
            PublishMode.Bootstrap =>
                "**Bootstrap.** Every package is packed at its first computed version. The publish job refuses if " +
                $"`{GitPublishState.DefaultBranch}` exists or the feed holds any version of these packages that does not sort below it; " +
                $"otherwise it pushes them all and creates `{GitPublishState.DefaultBranch}` recording them.",
            _ => throw new ArgumentOutOfRangeException(nameof(plan), plan.Mode, null)
        }).Append("\n\n");

        text.Append($"Commit `{plan.Commit}`, computed against ")
            .Append(plan.StateCommit is { } state ? $"`{GitPublishState.DefaultBranch}` at `{state}`" : "an empty record")
            .Append($". {computation.Affected.Count} of {computation.Packages.Count} package(s) are packed.\n\n");

        if (computation.Affected.Count == 0)
            return text.ToString();

        text.Append("| Package | Version | Why |\n|---|---|---|\n");
        foreach (var package in computation.Packages.Where(package => package.Affected))
            text.Append($"| {package.PackageId} | {PackProperties.VersionOf(package, label)} | {Cell(Reasons(package.Reasons))} |\n");

        return text.Append('\n').ToString();
    }

    /// <summary>What the publish job pushed and recorded, and why it stopped when it did.</summary>
    public static string Publish(PublishReport report)
    {
        var text = new StringBuilder()
            .Append(report.Succeeded ? $"### Published from `{report.Plan.Commit}`\n\n" : $"### Publish from `{report.Plan.Commit}` failed\n\n");

        if (report.Packages.Count > 0)
        {
            text.Append("| Package | Version | Outcome |\n|---|---|---|\n");
            foreach (var package in report.Packages)
                text.Append($"| {package.PackageId} | {package.Version} | {package.Kind}: {Cell(package.Detail)} |\n");
            text.Append('\n');
        }
        else
        {
            text.Append("Nothing was affected, so nothing was pushed.\n\n");
        }

        text.Append(report.RecordCommit is { } commit
            ? $"Recorded on `{GitPublishState.DefaultBranch}` as `{commit}`.\n\n"
            : "Nothing was recorded.\n\n");

        return (report.Failure is { } failure ? text.Append($"**Failure:** {Cell(failure)}\n\n") : text).ToString();
    }

    /// <summary>What the repair command wrote to <c>publish-state</c> (spec 150 FR-019).</summary>
    public static string Repair(string message, string recordCommit) =>
        $"### Repaired `{GitPublishState.DefaultBranch}`\n\n```\n{message}```\n\nRecorded as `{recordCommit}`.\n\n";

    private static string Reasons(IReadOnlyList<string> reasons) =>
        string.Join("; ", reasons.Take(ReasonsShown)) + (reasons.Count > ReasonsShown ? $"; and {reasons.Count - ReasonsShown} more" : string.Empty);

    private static string Cell(string text) => text.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal);
}
