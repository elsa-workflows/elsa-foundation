using System.Net;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// #5873: an Elsa 3 ForEach body reads its per-pass <c>CurrentValue</c> by name while the workflow declares a variable of
/// that name. Elsa 3 read the pass's item; the converted workflow publishes and reads the same items when it runs.
/// </summary>
public sealed class LoopPassVariableReadPublishTests(ITestOutputHelper output)
{
    [Fact]
    public async Task A_per_pass_read_shadowing_a_workflow_variable_publishes_and_reads_each_item()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync("import-foreach-pass-variable-read", ["foreach-pass-variable-read"]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        var workflow = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("foreach-pass-variable-read-v1"), "workflowVersionId"));

        var run = await host.RunAsync(workflow);
        output.WriteLine(run.Describe());

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Empty(run.Incidents);
        var frame = run.Workflow!.RootVariableFrame ?? throw new Xunit.Sdk.XunitException(run.Describe());
        Assert.True(frame.Values.TryGetValue("seen", out var seen), string.Join(", ", frame.Values.Keys));
        Assert.Equal("\"acme-a;acme-b;\"", seen.InlineValue?.GetRawText());
    }
}
