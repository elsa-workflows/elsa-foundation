using System.Net;
using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;
using Xunit.Abstractions;
using static Elsa3.Activities.Design.Import.Integration.Tests.PublishEventResumeTests;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// #3122 (spec 264): an imported Elsa 3 workflow that declares workflow variables publishes and runs. A variable with no
/// value starts empty and receives a captured output; a variable with a value starts with the value Elsa 3 gave it.
/// </summary>
public sealed class WorkflowVariablePublishTests(ITestOutputHelper output)
{
    [Fact]
    public async Task A_workflow_variable_without_a_value_publishes_and_receives_a_captured_Event_payload()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync("import-variable-captures-event", ["workflow-variable-captures-event", "publish-event-publisher-payload"]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        var waiter = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("workflow-variable-captures-event-v1"), "workflowVersionId"));
        var publisher = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("publish-event-publisher-payload-v1"), "workflowVersionId"));

        var waiting = await host.ExecuteAsync(waiter);
        await host.WaitForAsync(waiting, run => run.Activities.Any(state => IsEvent(state) && state.Status == ActivityExecutionStatus.Suspended));
        var published = await host.RunAsync(publisher);
        var run = await host.WaitForSettledAsync(waiting);
        output.WriteLine(run.Describe());

        Assert.True(published.Workflow?.Status == WorkflowExecutionStatus.Completed, published.Describe());
        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Empty(run.Incidents);
        Assert.Equal(Elsa3ObjectLiteralPayload, Variable(run, "approval"));
    }

    [Fact]
    public async Task Workflow_variables_with_values_start_with_the_values_Elsa_3_gave_them()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync("import-variable-starting-values", ["workflow-variable-starting-values"]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        var workflow = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("workflow-variable-starting-values-v1"), "workflowVersionId"));

        var run = await host.RunAsync(workflow);
        output.WriteLine(run.Describe());

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Equal("\"hello\"", Variable(run, "text"));
        Assert.Equal("5", Variable(run, "count"));
        Assert.Equal("true", Variable(run, "flag"));
        // Elsa 3 kept an Object variable's text unparsed.
        Assert.Equal("\"{\\u0022a\\u0022:1}\"", Variable(run, "raw"));
        Assert.Equal("""["x","y"]""", Variable(run, "names"));
        // Collections and dictionaries start as JSON, not text.
        Assert.Equal("""["a","b"]""", Variable(run, "list"));
        Assert.Equal("[1,2]", Variable(run, "counts"));
        // A type Elsa 4 does not know is an Object holding the JSON Elsa 3 deserialized.
        Assert.Equal("""{"id":1}""", Variable(run, "order"));
        Assert.Equal("""{"a":1}""", Variable(run, "dictionary"));
        Assert.Equal("""{"Accept":["application/json"]}""", Variable(run, "headers"));
    }

    /// <summary>A List&lt;String&gt; variable becomes a String list in Elsa 4 and still receives a captured array.</summary>
    [Fact]
    public async Task A_list_variable_receives_a_captured_array()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync("import-variable-captures-list", ["workflow-variable-captures-list", "publish-event-publisher-list"]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        var waiter = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("workflow-variable-captures-list-v1"), "workflowVersionId"));
        var publisher = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("publish-event-publisher-list-v1"), "workflowVersionId"));

        var waiting = await host.ExecuteAsync(waiter);
        await host.WaitForAsync(waiting, run => run.Activities.Any(state => IsEvent(state) && state.Status == ActivityExecutionStatus.Suspended));
        var published = await host.RunAsync(publisher);
        var run = await host.WaitForSettledAsync(waiting);
        output.WriteLine(run.Describe());

        Assert.True(published.Workflow?.Status == WorkflowExecutionStatus.Completed, published.Describe());
        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Empty(run.Incidents);
        Assert.Equal("""["a","b"]""", Variable(run, "names"));
    }

    /// <summary>The raw JSON of the variable in the run's root frame.</summary>
    private static string Variable(RunState run, string key)
    {
        var frame = run.Workflow?.RootVariableFrame ?? throw new Xunit.Sdk.XunitException($"The run has no root variable frame ({run.Describe()}).");
        Assert.True(frame.Values.TryGetValue(key, out var envelope), $"The root frame has no '{key}': {string.Join(", ", frame.Values.Keys)}.");
        return envelope.InlineValue?.GetRawText() ?? $"<{envelope.Presence}>";
    }
}
