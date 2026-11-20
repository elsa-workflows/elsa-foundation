using System.Net;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// #3287: an imported Elsa 3 RunJavaScript whose script reads a variable or an input binds those reads through its
/// <c>arguments</c> input. The converter declared that expression as <c>Object</c>, but Elsa 4's
/// <c>RunJavaScript.Arguments</c> is a <c>JsonElement</c>, and publication requires an exact match, so the import
/// succeeded and publishing was refused. These run the real path: import, publish and the Jint engine.
/// </summary>
public sealed class RunJavaScriptReadsPublishTests(ITestOutputHelper output)
{
    /// <summary>The repro from the issue: <c>return getVariable('greeting');</c>.</summary>
    [Fact]
    public async Task An_imported_script_that_reads_a_variable_publishes_and_runs()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync("import-script-reads-variable", ["script-reads-variable"]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        var workflow = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("script-reads-variable-v1"), "workflowVersionId"));

        var run = await host.RunAsync(workflow);
        output.WriteLine(run.Describe());

        AssertCompleted(run);
        Assert.Equal("\"hello\"", Variable(run, "reply"));
    }

    [Fact]
    public async Task An_imported_script_that_reads_an_input_publishes_and_runs()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync("import-script-reads-input", ["script-reads-input"]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        var workflow = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("script-reads-input-v1"), "workflowVersionId"));

        var run = await host.RunAsync(workflow, new JsonObject { ["name"] = "Ada" });
        output.WriteLine(run.Describe());

        AssertCompleted(run);
        Assert.Equal("\"Hello Ada\"", Variable(run, "reply"));
    }

    private static void AssertCompleted(RunState run)
    {
        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Empty(run.Incidents);
        Assert.All(run.Activities, state => Assert.Equal(ActivityExecutionStatus.Completed, state.Status));
        Assert.Contains("script", run.ExecutedNodeIds);
    }

    private static string Variable(RunState run, string key)
    {
        var frame = run.Workflow?.RootVariableFrame ?? throw new Xunit.Sdk.XunitException($"The run has no root variable frame ({run.Describe()}).");
        Assert.True(frame.Values.TryGetValue(key, out var envelope), $"The root frame has no '{key}': {string.Join(", ", frame.Values.Keys)}.");
        return envelope.InlineValue?.GetRawText() ?? $"<{envelope.Presence}>";
    }
}
