using System.Diagnostics;
using System.Net;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// The guidance for a refused reusable says to import it as a regular workflow and call it with Dispatch Workflow. This
/// proves that alternative: an imported Elsa 3 workflow that dispatches (or executes) another imported workflow by its
/// Elsa 3 definition id starts it, because the import keeps the Elsa 3 definition id as the Elsa 4 one.
/// </summary>
public sealed class DispatchWorkflowCallTests
{
    [Theory]
    [InlineData("dispatch-caller")]
    [InlineData("execute-caller")]
    public async Task An_imported_workflow_starts_another_imported_workflow_by_its_Elsa_3_definition_id(string callerFixture)
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync($"import-{callerFixture}", ["dispatch-target", callerFixture]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        Assert.Equal("dispatch-target", Elsa3ImportHost.Required(import.Item("dispatch-target-v1"), "workflowDefinitionId"));

        // The target is published first: publishing the caller pins the target's live published artifact.
        var target = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("dispatch-target-v1"), "workflowVersionId"));
        var caller = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item($"{callerFixture}-v1"), "workflowVersionId"));

        var callerRun = await host.RunAsync(caller);
        var targetRun = await host.WaitForSettledAsync(await TargetExecutionIdAsync(host, target));

        Assert.True(callerRun.Workflow?.Status == WorkflowExecutionStatus.Completed, callerRun.Describe());
        Assert.True(targetRun.Workflow?.Status == WorkflowExecutionStatus.Completed, targetRun.Describe());
        Assert.Contains(callerRun.Activities, state => state.Execution.ExecutableNodeId == "after" && state.Status == ActivityExecutionStatus.Completed);
        if (callerFixture == "execute-caller")
        {
            // Execute Workflow waits for the target, so the caller completes after it.
            Assert.True(targetRun.Workflow!.CompletedAt <= callerRun.Workflow!.CompletedAt,
                $"target {targetRun.Workflow.CompletedAt:O}, caller {callerRun.Workflow.CompletedAt:O}");
        }
    }

    [Fact]
    public async Task An_awaited_dispatch_result_capture_reads_the_childs_output_by_name()
    {
        // Spec 1245 FR-001, FR-006, US2 scenario 2: the Result capture converts to an Output capture into the same
        // Object and ObjectDictionary variables, and a script reading the child's output by name gets its value.
        var run = await RunCallerAsync("await-dispatch-reads-output", "output-target");

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Equal("\"approved\"", Variable(run, "decision"));
        Assert.Equal("\"approved\"", Variable(run, "decisionFromMap"));
        Assert.Equal("""{"Decision":"approved"}""", Variable(run, "address"));
    }

    [Fact]
    public async Task An_execute_workflow_result_capture_reads_every_Elsa_3_member()
    {
        // Spec 1245 FR-010, US3 scenario 4 with a child that completes.
        var run = await RunCallerAsync("execute-reads-result", "output-target");

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Equal("\"approved\"", Variable(run, "decision"));
        Assert.Equal("\"Finished\"", Variable(run, "status"));
        Assert.Equal("\"Finished\"", Variable(run, "subStatus"));
        var instanceId = System.Text.Json.JsonSerializer.Deserialize<string>(Variable(run, "instanceId"))!;
        Assert.False(string.IsNullOrWhiteSpace(instanceId));
        var whole = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Deserialize<string>(Variable(run, "whole"))!)!.AsObject();
        Assert.Equal(
            ["CorrelationId", "Output", "Status", "SubStatus", "WorkflowDefinitionVersionId", "WorkflowInstanceId"],
            whole.Select(property => property.Key).Order(StringComparer.Ordinal));
        Assert.Equal(instanceId, whole["WorkflowInstanceId"]!.GetValue<string>());
        Assert.Equal("Finished", whole["SubStatus"]!.GetValue<string>());
        Assert.Equal("approved", whole["Output"]!["Decision"]!.GetValue<string>());
        Assert.Null(whole["CorrelationId"]);
        Assert.Null(whole["WorkflowDefinitionVersionId"]);
    }

    [Fact]
    public async Task An_execute_workflow_result_after_a_faulted_child_reads_Faulted_and_no_output()
    {
        // Spec 1245 FR-002, FR-010, US3 scenario 4 with a child that faults: SubStatus reads Faulted, and a member of
        // the empty Output reads undefined rather than throwing.
        var run = await RunCallerAsync("execute-reads-faulted-result", "fault-target");

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Equal("\"Faulted\"", Variable(run, "subStatus"));
        Assert.Equal("\"Finished\"", Variable(run, "status"));
        Assert.Equal("{}", Variable(run, "credit"));
    }

    [Theory]
    [InlineData("execute-faulted-done")]
    [InlineData("await-dispatch-faulted-done")]
    [InlineData("sequence-execute-faulted")]
    public async Task A_faulted_child_still_runs_the_node_after_Done(string callerFixture)
    {
        // Spec 1245 FR-007, FR-009, US3 scenario 3, US4 scenario 2; the Sequence parent is the regression case.
        var run = await RunCallerAsync(callerFixture, "fault-target");

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Contains(run.Activities, state => state.Execution.ExecutableNodeId == "after" && state.Status == ActivityExecutionStatus.Completed);
    }

    private static async Task<RunState> RunCallerAsync(string callerFixture, string targetFixture)
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync($"import-{callerFixture}", [targetFixture, callerFixture]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item($"{targetFixture}-v1"), "workflowVersionId"));
        var caller = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item($"{callerFixture}-v1"), "workflowVersionId"));
        return await host.RunAsync(caller);
    }

    private static string Variable(RunState run, string key)
    {
        var frame = run.Workflow?.RootVariableFrame ?? throw new Xunit.Sdk.XunitException($"The run has no root variable frame ({run.Describe()}).");
        Assert.True(frame.Values.TryGetValue(key, out var envelope), $"The root frame has no '{key}': {string.Join(", ", frame.Values.Keys)}.");
        return envelope.InlineValue?.GetRawText() ?? $"<{envelope.Presence}>";
    }

    /// <summary>The execution started from <paramref name="target"/>'s artifact; the dispatch may start it after the caller's request.</summary>
    private static async Task<string> TargetExecutionIdAsync(Elsa3ImportHost host, PublishedWorkflow target)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            var executions = await host.ReadAsync(services => services.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync().AsTask());
            if (executions.SingleOrDefault(execution => execution.PinnedExecutable.ArtifactId == target.ArtifactId) is { } execution)
                return execution.WorkflowExecutionId;
            Assert.True(deadline.Elapsed < Elsa3ImportHost.SettleTimeout, "No execution of the dispatched target was started.");
            await Task.Delay(100);
        }
    }
}
