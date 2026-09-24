using Elsa.Cli.Worker;
using System.Text.Json;
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
    public void Script_check_does_not_accept_new_selection_flags()
    {
        var run = DotnetElsa.Run("persistence", "script-check", ".", "--host", Host,
            "--configuration-context", WorkerContextSources.WorkbenchJson);

        Assert.NotEqual(ToolExitCode.Success, run.ExitCode);
        Assert.Contains("--configuration-context", run.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Context_list_report_explains_selection_and_escapes_control_characters()
    {
        using var document = JsonDocument.Parse("""
            {"version":2,"status":"ok","exitCode":0,"command":"list","list":{"modules":[]},
             "configurationContext":{"source":"workbench-json-v1","environment":"Production","shell":"default",
               "resource":"primary","resolution":"resource","targetVerification":"not-performed","runtimeParity":"unobserved",
               "participants":[{"feature":"Feature\nInjected","module":"Runtime","resource":"primary",
                 "provider":"Sqlite","connectionReference":"Shared","selection":"RootDefault"}],
               "unresolved":["expected-connection-unchecked"]}}
            """);
        var response = new WorkerResponse { Tooling = document.RootElement.Clone() };
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(ToolExitCode.Success, Report.Render(WorkerCommands.List, response, output, error));
        Assert.Contains("Configuration context: workbench-json-v1", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Target verification: not-performed", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Feature\\u000AInjected", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Feature\nInjected", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("expected-connection-unchecked", output.ToString(), StringComparison.Ordinal);
    }
}
