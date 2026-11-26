using System.Net;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// #5697: the converter refuses a For loop's captured value read into a text input and says to convert it with a
/// JavaScript expression such as <c>String(…)</c>. A workflow that follows that guidance imports, publishes and runs.
/// </summary>
public sealed class LoopValueScriptPublishTests(ITestOutputHelper output)
{
    [Fact]
    public async Task A_loop_value_read_through_a_script_conversion_publishes_and_runs()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync("import-for-loop-value-script", ["for-loop-value-script"]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        var workflow = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("for-loop-value-script-v1"), "workflowVersionId"));

        var run = await host.RunAsync(workflow);
        output.WriteLine(run.Describe());

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Empty(run.Incidents);
        var frame = run.Workflow!.RootVariableFrame ?? throw new Xunit.Sdk.XunitException(run.Describe());
        Assert.True(frame.Values.TryGetValue("last", out var last), string.Join(", ", frame.Values.Keys));
        Assert.Equal("\"3\"", last.InlineValue?.GetRawText());
    }

    /// <summary>
    /// #5849: the same loop inside a reusable activity. The template compiles, the wrapper that places it publishes,
    /// and the placed loop body reads the loop's iteration value at run time.
    /// </summary>
    [Fact]
    public async Task A_loop_value_read_inside_a_reusable_activity_publishes_and_runs()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var (_, items) = await host.ImportAsync("import-reusable-for-loop-value-script", "reusable-for-loop-value-script");
        var reusable = Assert.Single(items);
        var wrapper = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(reusable, "workflowVersionId"));

        var run = await host.RunAsync(wrapper);
        output.WriteLine(run.Describe());

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Empty(run.Incidents);
    }
}
