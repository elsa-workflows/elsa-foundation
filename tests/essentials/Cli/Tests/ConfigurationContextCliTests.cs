using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class ConfigurationContextCliTests
{
    private static readonly string Host = DotnetElsa.Host("MinimalHost");

    [Theory]
    [InlineData("--resource", "primary", "invalid-selection")]
    [InlineData("--configuration-context", "unrecognized", "invalid-configuration-context")]
    [InlineData("--configuration-context", "workbench-json-v1", "invalid-selection")]
    public void Selector_errors_refuse_before_host_execution(string option, string value, string code)
    {
        var run = DotnetElsa.Run("persistence", "list", "--host", Host, option, value);

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains(code, run.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WorkerContextSources.WorkbenchJson)]
    [InlineData(WorkerContextSources.WorkbenchJsonEnvironment)]
    public void Explicit_context_is_routed_to_host_and_composer_free_host_refuses(string source)
    {
        var run = DotnetElsa.Run("persistence", "list", "--host", Host,
            "--configuration-context", source,
            "--shell", "default", "--resource", "primary");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("host-not-enrolled", run.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("context-operation-unavailable", run.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Context_plan_reaches_the_host_operation_without_legacy_projection()
    {
        var run = DotnetElsa.Run("persistence", "plan", "--host", Host,
            "--provider", "Sqlite", "--from-host",
            "--configuration-context", WorkerContextSources.WorkbenchJson,
            "--shell", "default", "--resource", "primary");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("host-not-enrolled", run.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("context-operation-unavailable", run.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WorkerContextSources.WorkbenchJson, WorkerCommands.Plan)]
    [InlineData(WorkerContextSources.WorkbenchJson, WorkerCommands.Script)]
    [InlineData(WorkerContextSources.WorkbenchJson, WorkerCommands.Apply)]
    [InlineData(WorkerContextSources.WorkbenchJson, WorkerCommands.Validate)]
    [InlineData(WorkerContextSources.WorkbenchJson, WorkerCommands.PostMigrate)]
    [InlineData(WorkerContextSources.WorkbenchJsonEnvironment, WorkerCommands.Plan)]
    [InlineData(WorkerContextSources.WorkbenchJsonEnvironment, WorkerCommands.Script)]
    [InlineData(WorkerContextSources.WorkbenchJsonEnvironment, WorkerCommands.Apply)]
    [InlineData(WorkerContextSources.WorkbenchJsonEnvironment, WorkerCommands.Validate)]
    [InlineData(WorkerContextSources.WorkbenchJsonEnvironment, WorkerCommands.PostMigrate)]
    public void Every_public_operation_uses_the_context_path_before_artifact_or_database_work(string source, string command)
    {
        using var output = new TempDirectory("elsa-context-refusal-");
        var arguments = new List<string>
        {
            "persistence", command, "--host", Host, "--provider", "PostgreSql",
            "--modules", "Acme.Widgets", "--configuration-context", source,
            "--shell", "default", "--resource", "primary"
        };
        if (command == WorkerCommands.Script)
            arguments.AddRange(["--output", output.Path]);
        var environment = new Dictionary<string, string>
        {
            ["ELSA_EF_CONNECTION"] = "Host=invalid;Password=context-command-canary"
        };

        var run = DotnetElsa.Run(environment, [.. arguments]);

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("host-not-enrolled", run.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("context-command-canary", run.Text, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output.Path));
    }

    [Fact]
    public void Script_check_does_not_accept_new_selection_flags()
    {
        var run = DotnetElsa.Run("persistence", "script-check", ".", "--host", Host,
            "--configuration-context", WorkerContextSources.WorkbenchJson);

        Assert.NotEqual(ToolExitCode.Success, run.ExitCode);
        Assert.Contains("--configuration-context", run.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Resource_intent_on_a_v1_only_host_never_downgrades_to_legacy_tooling()
    {
        using var host = new TempDirectory("elsa-legacy-context-host-");
        foreach (var file in Directory.EnumerateFiles(DotnetElsa.Host("LegacyHost")))
            File.Copy(file, host.File(Path.GetFileName(file)));
        var marker = host.File("v1-invoked.txt");
        var environment = new Dictionary<string, string> { ["ELSA_LEGACY_TOOLING_MARKER"] = marker };

        var legacy = DotnetElsa.Run(environment, "persistence", "list", "--host", host.Path);
        Assert.Equal(ToolExitCode.Success, legacy.ExitCode);
        Assert.Contains("0 module(s).", legacy.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(marker), "The fixture must reach its v1 entry point without resource intent.");

        File.Delete(marker);
        File.WriteAllText(host.File("appsettings.json"), """
            { "Elsa": { "Persistence": { "Resources": {
                "primary": { "Provider": "Sqlite", "ConnectionName": "secret-canary" }
            } } } }
            """);
        var list = DotnetElsa.Run(environment, "persistence", "list", "--host", host.Path);

        Assert.Equal(ToolExitCode.ResolutionFailure, list.ExitCode);
        Assert.Contains("context-capability-unavailable", list.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-canary", list.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(host.Path, list.Text, StringComparison.Ordinal);
        Assert.False(File.Exists(marker), "The resource-aware request must not call the v1 entry point.");

        var selected = DotnetElsa.Run(environment, "persistence", "list", "--host", host.Path,
            "--configuration-context", WorkerContextSources.WorkbenchJson,
            "--shell", "default", "--resource", "primary");
        Assert.Equal(ToolExitCode.ResolutionFailure, selected.ExitCode);
        Assert.Contains("context-capability-unavailable", selected.Text, StringComparison.Ordinal);
        Assert.False(File.Exists(marker));

        var output = host.File("sql");
        var script = DotnetElsa.Run(environment, "persistence", "script", "--host", host.Path,
            "--provider", "PostgreSql", "--modules", "Acme.Widgets", "--output", output);

        Assert.Equal(ToolExitCode.ResolutionFailure, script.ExitCode);
        Assert.Contains("context-capability-unavailable", script.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-canary", script.Text, StringComparison.Ordinal);
        Assert.False(File.Exists(marker));
        Assert.False(Directory.Exists(output));
    }

}
