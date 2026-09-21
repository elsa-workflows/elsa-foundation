using Elsa.Cli.Worker;
using System.Text.Json;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// One committed artifact, generated once from the fixture host, that every check in this class starts
/// from. Regenerating it per test would say nothing extra and would run the host's tooling once per case.
/// </summary>
public sealed class CommittedArtifact : IDisposable
{
    private readonly TempDirectory pristine = new("elsa-cli-pristine-");

    public CommittedArtifact()
    {
        var run = DotnetElsa.Run(
            "persistence", "script",
            "--host", DotnetElsa.Host("MinimalHost"),
            "--provider", "PostgreSql",
            "--modules", "Acme.Widgets",
            "--output", pristine.Path);
        if (run.ExitCode != ToolExitCode.Success)
            throw new InvalidOperationException($"The fixture artifact could not be generated: {run.Text}");
    }

    public void Dispose() => pristine.Dispose();

    /// <summary>A fresh copy for one test to age, edit or add to.</summary>
    public TempDirectory Copy()
    {
        var copy = new TempDirectory("elsa-cli-committed-");
        foreach (var file in Directory.EnumerateFiles(pristine.Path))
            File.Copy(file, copy.File(Path.GetFileName(file)));
        return copy;
    }
}

/// <summary>
/// <c>script-check</c> against a real packaged host (spec 171 User Story 4). The classification itself is
/// pinned by <see cref="ScriptCheckClassificationTests"/>; these prove the command reaches it — that it
/// regenerates from the committed plan rather than from anything typed on the command line, and that the
/// exit code an operator's CI job reads is the one the check decided.
/// </summary>
public sealed class ScriptCheckCliTests(CommittedArtifact artifact) : IClassFixture<CommittedArtifact>, IDisposable
{
    private const string ScriptFile = "01-acme-widgets.sql";

    private readonly TempDirectory committed = artifact.Copy();

    public void Dispose() => committed.Dispose();

    [Fact]
    public void An_artifact_that_still_matches_the_host_passes()
    {
        var run = Check();

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Contains("up to date", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hand_edited_statement_fails_with_the_file_named()
    {
        var path = committed.File(ScriptFile);
        File.WriteAllText(path, File.ReadAllText(path).Replace("character varying(128)", "character varying(200)", StringComparison.Ordinal));

        var run = Check();

        Assert.Equal(ToolExitCode.NegativeResult, run.ExitCode);
        Assert.Contains("SQL differs", run.Error, StringComparison.Ordinal);
        Assert.Contains(ScriptFile, run.Error, StringComparison.Ordinal);
        Assert.Contains("a statement changed", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The artifact aged by one migration: the module has moved on since it was generated. Reported as out
    /// of date rather than as an edit, because the two call for different work.
    /// </summary>
    [Fact]
    public void A_module_that_gained_a_migration_since_generation_fails_as_stale()
    {
        Age();

        var run = Check();

        Assert.Equal(ToolExitCode.NegativeResult, run.ExitCode);
        Assert.Contains("SQL differs", run.Error, StringComparison.Ordinal);
        Assert.Contains("out of date", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("a statement changed", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sql_file_the_plan_does_not_name_fails_as_an_orphan()
    {
        File.WriteAllText(committed.File("09-removed-module.sql"), "SELECT 1;\n");

        var run = Check();

        Assert.Equal(ToolExitCode.NegativeResult, run.ExitCode);
        Assert.Contains("09-removed-module.sql", run.Error, StringComparison.Ordinal);
        Assert.Contains("orphan", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A package upgraded with no migration change: every statement is identical and the manifest is not.
    /// Still exit 1 — the committed artifact no longer describes what the host pins — but reported as the
    /// kind that needs a regenerate and nothing applied.
    /// </summary>
    [Fact]
    public void A_version_that_moved_with_identical_sql_fails_as_a_manifest_difference()
    {
        Rewrite(plan => plan["efCoreVersion"] = "10.0.0-not-what-this-host-pins");

        var run = Check();

        Assert.Equal(ToolExitCode.NegativeResult, run.ExitCode);
        Assert.Contains("manifest versions differ, SQL identical", run.Error, StringComparison.Ordinal);
        Assert.Contains("efCoreVersion", run.Error, StringComparison.Ordinal);
        Assert.Contains("nothing new to apply", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both at once. The report must be the one that asks for review: a changed statement reported as a
    /// routine version bump is the failure this classification exists to prevent.
    /// </summary>
    [Fact]
    public void A_version_that_moved_never_masks_a_changed_statement()
    {
        Rewrite(plan => plan["efCoreVersion"] = "10.0.0-not-what-this-host-pins");
        var path = committed.File(ScriptFile);
        File.WriteAllText(path, File.ReadAllText(path).Replace("character varying(128)", "character varying(200)", StringComparison.Ordinal));

        var run = Check();

        Assert.Contains("SQL differs", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("manifest versions differ", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_directory_with_no_plan_is_refused_rather_than_reported_as_matching()
    {
        File.Delete(committed.File(MigrationPlan.FileName));

        var run = Check();

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("plan-missing", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The check regenerates from the committed plan, so a manifest naming a module this host does not
    /// declare is a resolution failure rather than a pass over the modules that do resolve.
    /// </summary>
    [Fact]
    public void A_plan_naming_a_module_the_host_no_longer_declares_is_refused()
    {
        Rewrite(plan => plan["modules"]![0]!["module"] = "Contoso.Gone");

        var run = Check();

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("unknown-module", run.Error, StringComparison.Ordinal);
    }

    /// <summary>The check takes no selection of its own: the plan is what the artifact claims to be (FR-044).</summary>
    [Fact]
    public void The_check_takes_no_module_selection_of_its_own()
    {
        var run = DotnetElsa.Run("persistence", "script-check", committed.Path, "--host", DotnetElsa.Host("MinimalHost"), "--modules", "Acme.Widgets");

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("usage", run.Error, StringComparison.Ordinal);
    }

    private CliRun Check() =>
        DotnetElsa.Run("persistence", "script-check", committed.Path, "--host", DotnetElsa.Host("MinimalHost"));

    /// <summary>
    /// Ages the artifact by one migration: the state a directory is in when a module gained a migration
    /// after it was last generated — the plan records the older set and the SQL stops at it.
    /// </summary>
    private void Age()
    {
        var path = committed.File(ScriptFile);
        var sql = File.ReadAllText(path);
        File.WriteAllText(path, sql[..sql.IndexOf("20260102000000_AddLabel", StringComparison.Ordinal)]);
        Rewrite(plan =>
        {
            var migrations = plan["modules"]![0]!["migrations"]!;
            migrations["to"] = "20260101000000_Initial";
            migrations["count"] = 1;
            migrations["ids"] = new System.Text.Json.Nodes.JsonArray("20260101000000_Initial");
        });
    }

    private void Rewrite(Action<System.Text.Json.Nodes.JsonNode> edit)
    {
        var path = committed.File(MigrationPlan.FileName);
        var plan = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        edit(plan);
        File.WriteAllText(path, plan.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
