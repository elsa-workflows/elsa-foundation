using System.Net;
using System.Text.Json.Nodes;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Scripting.Activities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

public sealed class RunJavaScriptSetsWorkflowStateRunTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Local_class_members_and_labels_named_like_helpers_import_and_run_unchanged()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var workflow = await ImportBody(host, "class Local { getCorrelationId = 7; setCorrelationId(value) { return value; } } setOutput: { break setOutput; } setOutput('Local', new Local().setCorrelationId('local')); return 1;");
        var run = await host.RunAsync(workflow);
        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Null(run.Workflow.CorrelationId);
        Assert.Equal("local", Assert.Single(await Values(host, run.Workflow.WorkflowExecutionId), value => value.ValueId == "output:Local").InlineValue!.Value.GetString());
    }

    [Fact]
    public async Task Imported_body_publishes_runs_and_commits_outputs_identity_and_variable_together()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync("import-script-state", ["script-workflow-state"]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        var workflow = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("script-workflow-state-v1"), "workflowVersionId"));
        var run = await host.RunAsync(workflow);
        output.WriteLine(run.Describe());
        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Empty(run.Incidents);
        Assert.Equal("new", run.Workflow.CorrelationId);
        Assert.Equal("Ready", run.Workflow.SystemMetadata[RuntimeMetadataKeys.InstanceName]);
        Assert.Equal("15", run.Workflow.RootVariableFrame!.Values["total"].InlineValue!.Value.GetRawText());
        Assert.Contains(run.Activities, state => state.Metadata.ContainsKey("workflowStateEffects"));
        var values = await Values(host, run.Workflow.WorkflowExecutionId);
        Assert.Equal(15, Assert.Single(values, value => value.ValueId == "output:Total").InlineValue!.Value.GetProperty("Amount").GetInt32());
        var version = await host.GetJsonAsync($"design/workflows/versions/{workflow.VersionId}");
        var state = version["state"]!.DeepClone();
        var catalog = await host.GetJsonAsync($"design/activities/definitions?search={typeof(RunJavaScript).FullName}");
        var activity = Assert.Single(catalog["items"]!.AsArray(), item => (string?)item?["definition"]?["activityTypeKey"] == typeof(RunJavaScript).FullName);
        state["rootActivity"] = new JsonObject
        {
            ["nodeId"] = "native", ["activityVersionId"] = Elsa3ImportHost.Required(activity!, "definition", "headVersionId"), ["outputs"] = new JsonArray(),
            ["inputs"] = new JsonArray(new JsonObject { ["referenceKey"] = "script", ["value"] = new JsonObject
            {
                ["expressionType"] = "Literal",
                ["value"] = "variables.total = 15; workflowState.setOutput('Total', {Amount: variables.total}); workflowState.setCorrelationId('new'); workflowState.setInstanceName('Ready'); return [workflowState.getCorrelationId(), workflowState.getInstanceName()];"
            } })
        };
        var nativeDefinition = await host.PostAsync("design/workflows/definitions/submit", new JsonObject { ["name"] = "Native equivalent", ["state"] = state });
        var nativeRun = await host.RunAsync(await host.PublishWorkflowAsync(Elsa3ImportHost.Required(nativeDefinition, "version", "id")));
        Assert.Equal(WorkflowExecutionStatus.Completed, nativeRun.Workflow!.Status);
        Assert.Equal(run.Workflow.CorrelationId, nativeRun.Workflow.CorrelationId);
        Assert.Equal(run.Workflow.SystemMetadata[RuntimeMetadataKeys.InstanceName], nativeRun.Workflow.SystemMetadata[RuntimeMetadataKeys.InstanceName]);
        var nativeValues = await Values(host, nativeRun.Workflow.WorkflowExecutionId);
        Assert.Equal(Assert.Single(values, value => value.ValueId == "output:Total").InlineValue!.Value.GetRawText(),
            Assert.Single(nativeValues, value => value.ValueId == "output:Total").InlineValue!.Value.GetRawText());
        Assert.Equal(run.Activities.Single(item => item.Execution.AuthoredActivityId == "seed").Completion!.Result.InlineValue!.Value.GetRawText(),
            nativeRun.Activities.Single(item => item.Execution.AuthoredActivityId == "native").Completion!.Result.InlineValue!.Value.GetRawText());
    }

    [Fact]
    public async Task Nested_pending_getters_and_short_circuit_computed_guards_execute_once_after_import()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var workflow = await ImportBody(host, """
            let calls=0; const blank=' ';
            false && setCorrelationId(blank);
            setCorrelationId((calls++, 'new'));
            setCorrelationId(getCorrelationId());
            setWorkflowInstanceName(getCorrelationId());
            setWorkflowInstanceName(getWorkflowInstanceName());
            const result=setOutput('Order', [calls, getCorrelationId(), getWorkflowInstanceName()]);
            setOutput('SetterUndefined', result === undefined); return 1;
            """);
        var run = await host.RunAsync(workflow);
        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        var values = await Values(host, run.Workflow.WorkflowExecutionId);
        Assert.Equal("[1,\"new\",\"new\"]", Assert.Single(values, value => value.ValueId == "output:Order").InlineValue!.Value.GetRawText());
        Assert.True(Assert.Single(values, value => value.ValueId == "output:SetterUndefined").InlineValue!.Value.GetBoolean());
    }

    [Theory]
    [InlineData("throw new Error('body failed');")]
    [InlineData("const blank=' '; try { setCorrelationId(blank); } catch(e) {} return 1;")]
    [InlineData("const blank=''; try { setWorkflowInstanceName(blank); } catch(e) {} return 1;")]
    public async Task Imported_faults_and_caught_computed_guards_roll_back_mixed_state(string ending)
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var workflow = await ImportBody(host, "setVariable('total',15); setOutput('Total',42); setCorrelationId('new'); setWorkflowInstanceName('Ready'); " + ending);
        var run = await host.RunAsync(workflow);
        Assert.Contains(run.Activities, state => state.Status == ActivityExecutionStatus.Faulted);
        Assert.Null(run.Workflow!.CorrelationId);
        Assert.False(run.Workflow.SystemMetadata.ContainsKey(RuntimeMetadataKeys.InstanceName));
        Assert.Equal("10", run.Workflow.RootVariableFrame!.Values["total"].InlineValue!.Value.GetRawText());
        Assert.DoesNotContain(await Values(host, run.Workflow.WorkflowExecutionId), value => value.ValueId == "output:Total");
    }

    [Fact]
    public async Task Imported_body_cannot_declassify_a_protected_read_after_an_existing_wait()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var workflow = await ImportBody(host, "const n=getVariable('total'); setVariable('total',99); setOutput('Total',n); setCorrelationId('new'); return n;", wait: true);
        var id = await host.ExecuteAsync(workflow);
        await host.WaitForAsync(id, run => run.Activities.Any(item => item.Status == ActivityExecutionStatus.Suspended));
        // Set up a real protected source on the idle retained run. The resumed imported body uses production stores,
        // session policy combination, script adapter and checkpoint acceptance, with no substituted evaluator.
        await host.ReadAsync(async services =>
        {
            var store = services.GetRequiredService<IWorkflowExecutionStateStore>();
            var retained = (await store.FindAsync(id))!;
            var frame = retained.RootVariableFrame!;
            var values = frame.Values.ToDictionary(pair => pair.Key, pair => pair.Value);
            var previous = values["total"];
            values["total"] = ValueEnvelope.Inline(previous.Type, previous.InlineValue!.Value,
                new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true));
            return await store.SaveAsync(retained with { RootVariableFrame = new VariableFrameState(frame.FrameId, frame.ScopeId,
                frame.ActivationId, frame.ParentFrameId, frame.Kind, values, frame.Revision, frame.Status, frame.Versions) });
        });
        await host.PostAsync("runtime/workflows/stimuli", new JsonObject { ["stimulusType"] = EventStimulus.StimulusType,
            ["stimulusHash"] = EventStimulus.Hash("order.approved"), ["mode"] = "ResumeOnly", ["input"] = new JsonObject { ["eventName"] = "order.approved" } });
        var run = await host.WaitForSettledAsync(id);
        Assert.Contains(run.Activities, item => item.Status == ActivityExecutionStatus.Faulted);
        Assert.Null(run.Workflow!.CorrelationId);
        Assert.Equal(10, run.Workflow.RootVariableFrame!.Values["total"].InlineValue!.Value.GetInt32());
        Assert.DoesNotContain(await Values(host, id), value => value.ValueId == "output:Total");
    }

    [Theory]
    [InlineData("return 1;", true)]
    [InlineData("setCorrelationId('new'); return 1;", false)]
    public async Task Reusable_graph_retains_known_read_only_bodies_and_refuses_state_bypass(string body, bool ready)
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var collection = JsonNode.Parse(Elsa3ImportHost.Collection("script-workflow-state"))!;
        collection[0]!["options"]!["usableAsActivity"] = true;
        collection[0]!["root"]!["activities"]![0]!["script"]!["expression"]!["value"] = body;
        var handle = await host.UploadCollectionJsonAsync(collection.ToJsonString());
        var (plan, _) = await host.AnalyzeAsync(handle);
        var selection = await host.SelectAsync(handle, plan, "script-workflow-state-v1");
        Assert.True((bool?)selection["isReady"], selection.ToJsonString());
        var applied = await host.ApplyAsync(handle, plan, "reusable-state", "script-workflow-state-v1");
        Assert.Equal(ready ? HttpStatusCode.Created : HttpStatusCode.UnprocessableEntity, applied.StatusCode);
        if (!ready) Assert.Contains("VF-IMP-005", applied.DiagnosticCodes);
    }

    private static async Task<PublishedWorkflow> ImportBody(Elsa3ImportHost host, string body, bool wait = false)
    {
        var collection = JsonNode.Parse(Elsa3ImportHost.Collection("script-workflow-state"))!;
        collection[0]!["root"]!["activities"]![0]!["script"]!["expression"]!["value"] = body;
        if (wait)
        {
            var first = JsonNode.Parse(Elsa3ImportHost.Collection("publish-event-waiter"))![0]!["root"]!["activities"]![0]!.DeepClone();
            collection[0]!["root"]!["activities"]!.AsArray().Insert(0, first);
            collection[0]!["root"]!["connections"]!.AsArray().Add(JsonNode.Parse("""{"source":{"activity":"wait","port":"Done"},"target":{"activity":"seed","port":"In"}}"""));
        }
        var handle = await host.UploadCollectionJsonAsync(collection.ToJsonString());
        var (plan, _) = await host.AnalyzeAsync(handle);
        await host.SelectAsync(handle, plan, "script-workflow-state-v1");
        var applied = await host.ApplyAsync(handle, plan, "state-body", "script-workflow-state-v1");
        Assert.Equal(HttpStatusCode.Created, applied.StatusCode);
        var version = Elsa3ImportHost.Required(applied.Body["sources"]![0]!, "workflowVersionId");
        return await host.PublishWorkflowAsync(version);
    }

    private static async Task<IReadOnlyCollection<DurableValueState>> Values(Elsa3ImportHost host, string id)
    {
        await using var scope = host.Shell.ServiceProvider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDurableValueStateStore>().ListAllDurableValueStatesAsync(id);
    }
}
