using System.Text.Json;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Pending_command_inspection_and_start_replay_preserve_owned_JSON_context(bool inspect, bool invalidConstructor)
    {
        var now = DateTimeOffset.UtcNow;
        var state = new RuntimeModelExecutionState(1, new("tenant-a"), "queued-execution", "tenant-a", new("elsa.bpmn", 1),
            Material().Identity, "incarnation", 1, RuntimeModelLifecycle.Running, new("bpmn-runtime", "operator"), now, now,
            JsonSerializer.SerializeToElement(new { }), null);
        var queue = new BrokenPendingQueue(invalidConstructor);
        var runtime = new BpmnModelRuntime(null!, null!, null!, null!, null!, new InspectionAccess(), null!, TimeProvider.System,
            queue, states: new InspectionReader(state));
        var error = await Assert.ThrowsAsync<BpmnModelValidationException>(() => inspect
            ? runtime.FindCommandAsync(state.ExecutionId, "queued-request", new("operator", "tenant-a")).AsTask()
            : InspectStart());
        if (invalidConstructor) Assert.IsAssignableFrom<ArgumentException>(error.InnerException);
        else Assert.IsAssignableFrom<JsonException>(error.InnerException);
        Assert.Contains("queued-request", error.Message);
        Assert.Contains(inspect ? state.ExecutionId : "bpmn:", error.Message);
        Assert.Contains("broken-queued-item", error.Message);
        Assert.Equal(1, queue.Reads);

        async Task InspectStart() => _ = await runtime.StartAsync("source", "queued-request", null, new("operator", "tenant-a"));
    }

    [Fact]
    public async Task Legacy_worker_without_an_explicit_closed_input_contract_refuses_with_binding_and_pin_context()
    {
        await using var host = await OpenAsync(activityIds: ["round-three-worker"]);
        var worker = new WorkflowExecutable(WorkflowExecutionHarness.Identity,
            WorkflowExecutionHarness.NewFaultingNode("round-three-worker", "Expected worker fault"),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), WorkflowExecutionHarness.Timestamp, new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
        var published = await host.PublishAsync(worker, "legacy-contract-worker");
        await host.StartPublishedAsync(published, "legacy-contract-native-control", partition: new(PersistenceScope.DefaultValue));
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await host.Services.GetRequiredService<IWorkflowExecutionStateStore>()
            .FindAsync("legacy-contract-native-control"))!.Status);
        await using var scope = await ScopeAsync(host);
        var definition = ServiceErrorMaterial("test.error").Definition.Deserialize<BpmnModelDefinition>(Web)!;
        var error = await Assert.ThrowsAsync<BpmnModelValidationException>(() => scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>()
            .PublishAsync(new(2, definition.Process, [], [new("worker", worker.Identity, "test.error")]),
                "legacy-contract-model", new("operator", null)).AsTask());
        Assert.Contains("worker", error.Message);
        Assert.Contains(worker.Identity.ArtifactId, error.Message);
        Assert.Contains("input contract", error.Message);
    }

    [Fact]
    public async Task A_missing_timed_work_binding_names_its_process_element_and_binding()
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var good = TimedHumanMaterial().Definition.Deserialize<BpmnModelDefinition>(Web)!;
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        _ = await runtime.PublishAsync(good, "valid-timed-inventory", new("operator", null));
        var error = await Assert.ThrowsAsync<BpmnModelValidationException>(() => runtime
            .PublishAsync(new(4, good.Process, ["unrelated-work"], null, good.TimerBindings), "missing-timed-inventory", new("operator", null)).AsTask());
        Assert.Contains("timed-human", error.Message);
        Assert.Contains("review", error.Message);
        Assert.Contains("review-work", error.Message);
    }

    private sealed class BrokenPendingQueue(bool invalidConstructor) : IWorkflowSchedulerWorkQueue
    {
        public int Reads { get; private set; }
        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default)
        {
            Reads++;
            var now = DateTimeOffset.UtcNow;
            var payload = JsonSerializer.SerializeToElement("invalid-model-command");
            if (invalidConstructor)
            {
                var command = new RuntimeModelExecutionCommand(Material().Identity, "incarnation", "queued-request", "start",
                    JsonSerializer.SerializeToElement(new { }), new("bpmn-runtime", "operator"), "tenant-a");
                var value = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(command, Web))!;
                value["inputKind"] = "";
                payload = JsonSerializer.SerializeToElement(value);
            }
            var item = new RuntimeSchedulerWorkItem("broken-queued-item", query.WorkflowExecutionId, "queued-request",
                WorkflowExecutionCommandKind.ExecuteModel, "envelope", "idempotency", now, now,
                payload: payload);
            return ValueTask.FromResult(new RuntimeStorePage<RuntimeSchedulerWorkItem>(query, [item], null));
        }
        public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem item, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string executionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
