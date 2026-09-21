using Elsa.Cli.Worker;
using System.Text.Json;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// The tool run end to end against a host that carries shell configuration beside its build output (spec
/// 171 FR-035–FR-039, FR-028, FR-064; User Story 1 scenarios 4 and 5). The fixture host's
/// <c>shells.json</c> has one shell per case, so each run names the shell it is about.
/// </summary>
public sealed class ProviderAgreementCliTests : IDisposable
{
    private const string Runtime = "WorkflowsRuntimeEntityFrameworkCore";
    private const string Bookmarks = "WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence";

    private static readonly string Host = DotnetElsa.Host("ShellsHost");

    private readonly TempDirectory output = new("elsa-cli-agreement-");

    public void Dispose() => output.Dispose();

    [Fact]
    public void Every_enabled_feature_agreeing_produces_the_artifact_and_records_the_check_as_run()
    {
        var run = Script("agree", "PostgreSql", "--modules", "Workflows.Runtime");

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Equal("checked", HostFact("providerAgreement"));
        Assert.Equal("agree", HostFact("shell"));
        Assert.Equal("Production", HostFact("environment"));
        Assert.Contains("environment-variable configuration overrides are invisible", HostFact("providerAgreementNote"), StringComparison.Ordinal);
    }

    /// <summary>User Story 1 scenario 5: the agreeing feature does not speak for the module.</summary>
    [Fact]
    public void A_feature_left_at_its_own_sqlite_default_is_reported_while_its_sibling_agrees()
    {
        var run = Script("unset-offender", "PostgreSql", "--modules", "Workflows.Runtime");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("provider-disagreement", run.Text, StringComparison.Ordinal);
        Assert.Contains(Bookmarks, run.Text, StringComparison.Ordinal);
        Assert.Contains("'Workflows.Runtime'", run.Text, StringComparison.Ordinal);
        Assert.Contains("'Sqlite'", run.Text, StringComparison.Ordinal);
        Assert.False(File.Exists(output.File(MigrationPlan.FileName)), "A refused run must write nothing.");
    }

    [Fact]
    public void An_explicitly_disagreeing_feature_is_reported_by_name_module_and_value()
    {
        var run = Script("one-offender", "PostgreSql", "--modules", "Workflows.Runtime");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains(Bookmarks, run.Text, StringComparison.Ordinal);
        Assert.DoesNotContain($"'{Runtime}'", run.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The check runs under an ordinary <c>--modules</c> selection, not only under <c>--from-host</c>
    /// (FR-035): <c>plan</c> writes nothing and is still refused.
    /// </summary>
    [Fact]
    public void The_check_runs_under_any_selector_not_only_from_host()
    {
        var plan = DotnetElsa.Run("persistence", "plan", "--host", Host, "--provider", "PostgreSql", "--modules", "Workflows.Runtime", "--shell", "one-offender");

        Assert.Equal(ToolExitCode.ResolutionFailure, plan.ExitCode);
        Assert.Contains(Bookmarks, plan.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The no-<c>Provider</c>-setting edge case: mapped to two selected modules and skipped by the
    /// comparison entirely rather than flagged or defaulted to Sqlite.
    /// </summary>
    [Fact]
    public void The_dashboard_feature_declaring_no_provider_setting_is_skipped()
    {
        var run = Script("dashboard-only", "PostgreSql", "--modules", "Workflows.Runtime,Workflows.Design");

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Equal("checked", HostFact("providerAgreement"));
    }

    [Fact]
    public void A_feature_the_shell_disables_is_ignored()
    {
        Assert.Equal(ToolExitCode.Success, Script("disabled-offender", "PostgreSql", "--modules", "Workflows.Runtime").ExitCode);
    }

    /// <summary>
    /// CShells accepts an array-shaped <c>Features</c> section too, and a host that authored it must not
    /// read as "no feature enabled" — which would report a check as run having compared nothing.
    /// </summary>
    [Fact]
    public void An_array_shaped_features_section_is_read_rather_than_seen_as_empty()
    {
        var run = Script("array-shape", "PostgreSql", "--modules", "Workflows.Runtime");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains(Runtime, run.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--from-host</c> selects the modules the shell's enabled features map to — here through the
    /// dashboard feature's two <c>[UsesEfModule]</c> declarations as well as the runtime and design
    /// features' own.
    /// </summary>
    [Fact]
    public void From_host_selects_the_modules_the_shells_enabled_features_map_to()
    {
        var run = DotnetElsa.Run("persistence", "list", "--host", Host, "--from-host", "--shell", "agree");

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Contains("Workflows.Runtime", run.Output, StringComparison.Ordinal);
        Assert.Contains("Workflows.Design", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Secrets", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same selection driving a real artifact: <c>script</c> resolves <c>--from-host</c> twice — once to
    /// learn which assembly each module lives in, once to generate — and both must reach the same set.
    /// </summary>
    [Fact]
    public void From_host_scripts_exactly_the_modules_its_features_map_to()
    {
        var run = DotnetElsa.Run("persistence", "script", "--host", Host, "--provider", "PostgreSql", "--from-host", "--shell", "agree", "--output", output.Path);

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Equal(
            ["01-workflows-design.sql", "02-workflows-runtime.sql", MigrationPlan.FileName],
            Directory.EnumerateFiles(output.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Two_selectors_together_are_a_usage_error()
    {
        var run = DotnetElsa.Run("persistence", "list", "--host", Host, "--from-host", "--all");

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("cannot be combined", run.Text, StringComparison.Ordinal);
    }

    /// <summary>User Story 1 scenario 4: a name outside the 13-name vocabulary.</summary>
    [Fact]
    public void A_module_outside_the_vocabulary_is_refused_before_anything_is_written()
    {
        var run = Script("agree", "PostgreSql", "--modules", "Workflows.Management");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("Workflows.Management", run.Text, StringComparison.Ordinal);
        Assert.False(File.Exists(output.File(MigrationPlan.FileName)));
    }

    [Fact]
    public void A_shell_the_host_does_not_configure_is_refused_rather_than_matching_nothing()
    {
        var run = Script("no-such-shell", "PostgreSql", "--modules", "Workflows.Runtime");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("unknown-shell", run.Text, StringComparison.Ordinal);
        Assert.Contains("'agree' is configured.", run.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// FR-038's default, and the reason it is a constant rather than this process's own environment: the
    /// overlay read is the host's <c>Production</c> one even when the tool itself is told it is running in
    /// Development.
    /// </summary>
    [Fact]
    public void The_environment_defaults_to_production_and_never_to_the_tools_own()
    {
        var tooling = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["DOTNET_ENVIRONMENT"] = "Development"
        };

        var run = DotnetElsa.Run(tooling, "persistence", "script", "--host", Host, "--provider", "PostgreSql", "--modules", "Workflows.Runtime", "--shell", "overlay", "--output", output.Path);

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Equal("Production", HostFact("environment"));
    }

    /// <summary>
    /// The same shell, read without its overlay: the base file's Sqlite is what a Development host would
    /// see, so the run is refused. A missing overlay is not an error — it simply leaves the base in place.
    /// </summary>
    [Fact]
    public void A_named_environment_with_no_overlay_file_reads_the_base_file_alone()
    {
        var run = DotnetElsa.Run("persistence", "script", "--host", Host, "--provider", "PostgreSql", "--modules", "Workflows.Runtime", "--shell", "overlay", "--environment", "Development", "--output", output.Path);

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains(Runtime, run.Text, StringComparison.Ordinal);
        Assert.Contains("'Sqlite'", run.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The host's shell configuration is where connection strings live. Only <c>Provider</c> is ever lifted
    /// out of it, so a refusal that names a feature cannot carry the credential sitting beside its provider.
    /// </summary>
    [Fact]
    public void A_refusal_never_carries_a_connection_string_from_the_shell_configuration()
    {
        var run = Script("secrets", "PostgreSql", "--modules", "Secrets");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("SecretsEntityFrameworkCore", run.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-only-secret", run.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-db", run.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host with no shells configuration at all is the one case FR-039 records as <c>not-checked</c>, and
    /// the one case <c>--from-host</c> cannot answer.
    /// </summary>
    [Fact]
    public void A_host_with_no_shell_configuration_reports_not_checked_and_refuses_from_host()
    {
        var minimal = DotnetElsa.Host("MinimalHost");
        var script = DotnetElsa.Run("persistence", "script", "--host", minimal, "--provider", "PostgreSql", "--modules", "Acme.Widgets", "--output", output.Path);
        Assert.Equal(ToolExitCode.Success, script.ExitCode);
        Assert.Equal("not-checked", HostFact("providerAgreement"));

        var fromHost = DotnetElsa.Run("persistence", "list", "--host", minimal, "--from-host");
        Assert.Equal(ToolExitCode.ResolutionFailure, fromHost.ExitCode);
        Assert.Contains("shells-configuration-missing", fromHost.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>script-check</c> regenerates from the committed plan's own <c>host.shell</c> and
    /// <c>host.environment</c>, so the agreement check re-runs against exactly the configuration the
    /// artifact was produced from rather than against whichever shell happens to be first.
    /// </summary>
    [Fact]
    public void Script_check_regenerates_against_the_shell_and_environment_the_plan_records()
    {
        Assert.Equal(ToolExitCode.Success, Script("agree", "PostgreSql", "--modules", "Workflows.Runtime").ExitCode);

        var check = DotnetElsa.Run("persistence", "script-check", output.Path, "--host", Host);

        Assert.Equal(ToolExitCode.Success, check.ExitCode);
        Assert.Contains("up to date", check.Text, StringComparison.Ordinal);
    }

    private CliRun Script(string shell, string provider, params string[] selection) => DotnetElsa.Run(
        ["persistence", "script", "--host", Host, "--provider", provider, .. selection, "--shell", shell, "--output", output.Path]);

    private string? HostFact(string name)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(output.File(MigrationPlan.FileName)));
        return manifest.RootElement.GetProperty("host").GetProperty(name).GetString();
    }
}
