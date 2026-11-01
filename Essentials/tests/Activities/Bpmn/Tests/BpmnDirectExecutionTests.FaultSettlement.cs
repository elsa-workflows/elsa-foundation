using System.IO.Compression;
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_concurrent_service_fault_settles_every_original_start_without_another_semantic_decision(bool admitBoth)
    {
        await using var host = await OpenAsync(activityIds: Enumerable.Range(1, 32).Select(i => "fault-settlement-" + i).ToArray());
        var worker = new WorkflowExecutable(WorkflowExecutionHarness.Identity, WorkflowExecutionHarness.NewFaultingNode("fault-worker", "Expected failure"),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), WorkflowExecutionHarness.Timestamp, new Dictionary<string, string>(),
            inputContract: new(1, []), dependencies: null, incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
        _ = await host.PublishAsync(worker, "concurrent-fault-worker");
        await using var scope = await ScopeAsync(host);
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        var caller = new BpmnModelCaller("operator", null);
        var process = new BpmnProcessDefinition("concurrent-fault", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("work", BpmnElementTypes.ServiceTask, bindingRef: "worker"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("first", "start", "work"), new("second", "start", "work"), new("finish", "work", "end")]);
        var publication = await runtime.PublishAsync(new(2, JsonSerializer.SerializeToElement(process, Web), [], [new("worker", worker.Identity, "test.error")]),
            "concurrent-fault-model", caller);
        var started = await runtime.StartAsync(publication.SourceReferenceId, "concurrent-fault-start", null, caller);
        var original = (await runtime.FindExecutionAsync(started.ExecutionId, caller))!;
        var starts = BpmnDirectExecutionDriver.Read(original).Work.Select(w => w.NativeWork!).ToArray();
        Assert.Equal(2, starts.Length);
        var processor = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        await processor.ProcessAsync(new(workflowExecutionId: original.ExecutionId, intentKind: BpmnNativeWorkBoundary.StartKind, limit: admitBoth ? 2 : 1));
        await processor.ProcessAsync(new(intentKind: BpmnNativeWorkBoundary.ResultKind, limit: 1));
        await DrainModelAsync(host, original.ExecutionId);
        var fault = (await runtime.FindExecutionAsync(original.ExecutionId, caller))!;
        Assert.Equal("Faulted", fault.TerminalResult!.Value.GetProperty("status").GetString());
        Assert.Equal(RuntimeModelLifecycle.TerminalPendingSettlement, fault.Lifecycle);
        var responsibility = Assert.Single(BpmnDirectExecutionDriver.Read(fault).Cancellations!);
        Assert.False(responsibility.Settled);
        Assert.Empty(BpmnDirectExecutionDriver.Read(fault).Work);
        await SettleFaultAsync(host, scope.ServiceProvider, original.ExecutionId, starts, admitBoth ? 2 : 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_actual_legacy_fault_checkpoint_recovers_its_owed_start_without_reopening_the_fault(bool admitted)
    {
        await using (var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", admitted ? "ActualConcurrentAdmittedFault.sqlite.gz" : "ActualConcurrentFault.sqlite.gz")))
        await using (var zipped = new GZipStream(input, CompressionMode.Decompress))
        await using (var output = File.Create(database)) await zipped.CopyToAsync(output);
        await using var host = await OpenAsync(activityIds: Enumerable.Range(1, 32).Select(i => "legacy-fault-settlement-" + i).ToArray());
        await using var scope = await ScopeAsync(host);
        var reader = scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>();
        var before = Assert.Single((await reader.ListPageAsync(new())).Items);
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, before.Lifecycle);
        var owed = Assert.Single(BpmnDirectExecutionDriver.Read(before).Work).NativeWork!;
        Assert.Null(BpmnDirectExecutionDriver.Read(before).Cancellations);
        await SettleFaultAsync(host, scope.ServiceProvider, before.ExecutionId, [owed], admitted ? 2 : 1);
    }

    private static async Task SettleFaultAsync(WorkflowExecutionHarness host, IServiceProvider services, string executionId,
        IReadOnlyList<RuntimeModelNativeWorkOrigin> origins, int expectedWorkers)
    {
        var reader = services.GetRequiredService<IRuntimeModelExecutionReader>();
        var before = (await reader.FindAsync(executionId))!;
        var terminal = before.TerminalResult!.Value.GetRawText();
        var semantic = BpmnDirectExecutionDriver.Read(before).State;
        var processor = services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        var lookup = services.GetRequiredService<IPostCommitOutboxLookupStore>();
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await processor.ProcessAsync(new(limit: 100));
            await DrainModelAsync(host, executionId);
            var state = (await reader.FindAsync(executionId))!;
            var document = BpmnDirectExecutionDriver.Read(state);
            if (state.Lifecycle == RuntimeModelLifecycle.TerminalSettled && document.Cancellations?.All(c => c.Settled) == true &&
                (await Task.WhenAll(origins.Select(async origin => (await lookup.FindDurableAsync(origin.SourceOutboxItemId))!.Status)))
                    .All(status => status == RuntimePostCommitOutboxStatus.Delivered) &&
                (await Task.WhenAll(document.Cancellations.Select(async cancellation => (await lookup.FindDurableAsync(cancellation.SourceOutboxItemId))!.Status)))
                    .All(status => status == RuntimePostCommitOutboxStatus.Delivered)) break;
            await Task.Delay(1300);
        }
        var settled = (await reader.FindAsync(executionId))!;
        var final = BpmnDirectExecutionDriver.Read(settled);
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, settled.Lifecycle);
        Assert.Equal(terminal, settled.TerminalResult!.Value.GetRawText());
        Assert.Equal(JsonSerializer.Serialize(semantic, Web), JsonSerializer.Serialize(final.State, Web));
        Assert.Empty(final.Work);
        Assert.NotEmpty(final.Cancellations!);
        Assert.All(final.Cancellations!, c => Assert.True(c.Settled));
        Assert.Equal(expectedWorkers, (await services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync()).Count);
        foreach (var origin in origins)
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await lookup.FindDurableAsync(origin.SourceOutboxItemId))!.Status);
        foreach (var cancellation in final.Cancellations!)
        {
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await lookup.FindDurableAsync(cancellation.SourceOutboxItemId))!.Status);
            if (cancellation.NotAdmitted)
            {
                Assert.Null(await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(cancellation.Origin.WorkerExecutionId));
                Assert.NotNull(await reader.FindInputAsync(executionId, BpmnNativeWorkBoundary.RetirementInputId(cancellation)));
            }
            else Assert.NotNull(await reader.FindInputAsync(executionId, cancellation.Origin.ResultInputId));
        }
    }

    private static async Task DrainModelAsync(WorkflowExecutionHarness host, string executionId)
    {
        var now = DateTimeOffset.UtcNow;
        var id = "fault-settlement-drain:" + Guid.NewGuid().ToString("N");
        var actor = await host.Services.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(executionId,
            WorkflowExecutionActorActivationReason.Start, now, "fault-settlement", WorkflowExecutionActorCapabilities.InProcessMailbox,
            partition: new(PersistenceScope.DefaultValue)));
        var result = await actor.EnqueueAsync(new(id, executionId, new(id, executionId, WorkflowExecutionCommandKind.RunSchedulerWork,
            now, null, new Dictionary<string, string>()), id, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now,
            partition: new(PersistenceScope.DefaultValue)));
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.Status);
    }
}
