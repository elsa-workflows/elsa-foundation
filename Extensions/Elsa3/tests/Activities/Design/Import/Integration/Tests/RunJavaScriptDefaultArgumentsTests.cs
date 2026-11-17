using System.Net;
using System.Text.Json.Nodes;
using Elsa.Activities.Scripting.Activities;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// #3195: a RunJavaScript whose <c>arguments</c> input is left unbound runs on its declared default, the empty object.
/// CLR reconciliation used to read that <c>"{}"</c> default as a JSON string, publication pinned the string, and the
/// engine refused it ("JavaScript activity arguments must be one JSON object"). It was an Elsa 4 defect that an Elsa 3
/// import, which never sets <c>arguments</c>, only exposed. This host runs the real path: CLR reconciliation, publishing
/// and the Jint engine.
/// </summary>
public sealed class RunJavaScriptDefaultArgumentsTests(ITestOutputHelper output)
{
    [Fact]
    public async Task An_Elsa_4_RunJavaScript_with_unbound_arguments_publishes_and_runs()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var typeKey = typeof(RunJavaScript).FullName!;
        var catalog = await host.GetJsonAsync($"design/activities/definitions?search={Uri.EscapeDataString(typeKey)}");
        var activity = Assert.Single(catalog["items"]!.AsArray(), item => (string?)item?["definition"]?["activityTypeKey"] == typeKey);
        var definition = await host.PostAsync("design/workflows/definitions/submit", new JsonObject
        {
            ["name"] = "RunJavaScript with unbound arguments",
            ["state"] = new JsonObject
            {
                ["variables"] = new JsonArray(),
                ["inputs"] = new JsonArray(),
                ["outputs"] = new JsonArray(),
                ["rootActivity"] = new JsonObject
                {
                    ["nodeId"] = "script",
                    ["activityVersionId"] = Elsa3ImportHost.Required(activity, "definition", "headVersionId"),
                    ["outputs"] = new JsonArray(),
                    ["inputs"] = new JsonArray(new JsonObject
                    {
                        ["referenceKey"] = "script",
                        ["value"] = new JsonObject { ["value"] = "return typeof args;", ["expressionType"] = "Literal" }
                    })
                }
            }
        });
        var workflow = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(definition, "version", "id"));

        var run = await host.RunAsync(workflow);
        output.WriteLine(run.Describe());

        AssertCompleted(run);
    }

    /// <summary>The repro from the issue: an imported Elsa 3 RunJavaScript that sets no <c>arguments</c>.</summary>
    [Fact]
    public async Task An_imported_Elsa_3_RunJavaScript_without_arguments_publishes_and_runs()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync("import-null-members-script", ["null-members-script"]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        var workflow = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("null-members-script-v1"), "workflowVersionId"));

        var run = await host.RunAsync(workflow);
        output.WriteLine(run.Describe());

        AssertCompleted(run);
    }

    private static void AssertCompleted(RunState run)
    {
        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Empty(run.Incidents);
        Assert.All(run.Activities, state => Assert.Equal(ActivityExecutionStatus.Completed, state.Status));
        Assert.Contains("script", run.ExecutedNodeIds);
    }
}
