using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Compensation;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Fact]
    public async Task An_original_queued_cancelled_result_recovers_without_waiting_for_its_obsolete_native_retry()
    {
        await using (var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ActualDelayedCompensation.sqlite.gz")))
        await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        await using (var output = File.Create(database)) await gzip.CopyToAsync(output);
        Assert.Equal("dedc7041baf31ad6d0b3de251049de7d55ff421dbe7e65e3059b6e35f9bf9e63",
            Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(database))));
        await using var host = await OpenAsync(admitNewExecutions: false,
            configure: services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(TimeProvider.System)));
        await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        const string model = "bpmn:a29cc5c675038cc802eabef05ca3c36a517ddbf17c66304badea80379b1e56cd";
        var reader = services.GetRequiredService<IRuntimeModelExecutionReader>();
        var before = (await reader.FindAsync(model))!;
        Assert.Equal(RuntimeModelLifecycle.TerminalPendingSettlement, before.Lifecycle);
        var origin = Assert.Single(BpmnDirectExecutionDriver.Read(before).Cancellations!).Origin;
        var workflows = services.GetRequiredService<IWorkflowExecutionStateStore>();
        var originalWorker = JsonSerializer.Serialize(await workflows.FindAsync(origin.WorkerExecutionId), Web);
        var lookup = services.GetRequiredService<IPostCommitOutboxLookupStore>();
        var retry = (await lookup.FindUndeliveredDurableAsync(origin.WorkerExecutionId, [BpmnNativeWorkBoundary.ResultKind]))!;
        Assert.Equal(RuntimePostCommitIntentKinds.EnqueueSchedulerWork, retry.Intent.Kind);
        var originalRetry = JsonSerializer.Serialize(retry, Web);
        var queue = services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var queued = Assert.Single((await queue.ListAsync(new(model))).Items);
        var originalResult = queued.Payload!.Value.Deserialize<RuntimeModelExecutionCommand>(Web)!.Payload;
        Assert.Null(await reader.FindInputAsync(model, queued.CommandId));
        await DrainModelAsync(host, model);
        var settled = (await reader.FindAsync(model))!;
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, settled.Lifecycle);
        Assert.Equal("Cancelled", settled.TerminalResult!.Value.GetProperty("status").GetString());
        Assert.Equal("Accepted", (await reader.FindInputAsync(model, queued.CommandId))!.Result.GetProperty("status").GetString());
        Assert.True(JsonElement.DeepEquals(originalResult,
            JsonSerializer.SerializeToElement(Assert.Single(BpmnDirectExecutionDriver.Read(settled).NativeResults!), Web)));
        Assert.Equal(originalWorker, JsonSerializer.Serialize(await workflows.FindAsync(origin.WorkerExecutionId), Web));
        Assert.Equal(originalRetry, JsonSerializer.Serialize(await lookup.FindDurableAsync(retry.OutboxItemId), Web));
        Assert.Equal(before.Executable, settled.Executable);
        Assert.Equal(before.MaterialIncarnationId, settled.MaterialIncarnationId);
        Assert.True(WorkflowExecutionAuthoritySnapshot.Matches(settled.Authority,
            before.Authority.SystemIdentity, before.Authority.RootInitiator, before.Authority.Metadata));
    }

    [Fact]
    public async Task Native_cancellation_settles_before_an_obsolete_compensation_retry_becomes_due()
    {
        await using var host = await OpenAsync(configure: services =>
        {
            new ActivitiesCompensationFeature().ConfigureServices(services);
            new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(TimeProvider.System));
        }, activityIds: Enumerable.Range(1, 32).Select(i => "delayed-compensation-" + i).ToArray());
        var worker = new WorkflowExecutable(WorkflowExecutionHarness.Identity, DelayedCompensationWorker(),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow, new Dictionary<string, string>(),
            inputContract: new(1, []), dependencies: null, incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
        await host.PublishAsync(worker, "delayed-compensation-worker");
        await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        var runtime = services.GetRequiredService<IBpmnModelRuntime>();
        var reader = services.GetRequiredService<IRuntimeModelExecutionReader>();
        var lookup = services.GetRequiredService<IPostCommitOutboxLookupStore>();
        var processor = services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        var queue = services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var caller = new BpmnModelCaller("operator", null);
        var process = new BpmnProcessDefinition("delayed-compensation", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("work", BpmnElementTypes.ServiceTask, bindingRef: "worker"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("start-work", "start", "work"), new("work-end", "work", "end")]);
        var publication = await runtime.PublishAsync(new(2, JsonSerializer.SerializeToElement(process, Web), [], [new("worker", worker.Identity, "test.error")]),
            "delayed-compensation-model", caller);
        var started = await runtime.StartAsync(publication.SourceReferenceId, "delayed-compensation-start", null, caller);
        var before = (await reader.FindAsync(started.ExecutionId))!;
        var origin = Assert.Single(BpmnDirectExecutionDriver.Read(before).Work).NativeWork!;
        await processor.ProcessAsync(new(limit: 100, workflowExecutionId: before.ExecutionId, intentKind: BpmnNativeWorkBoundary.StartKind));
        // The delivery can leave the admitted worker's protected continuations queued. Drive its ordinary
        // outbox and scheduler until the future retry is the remaining source, rather than assuming inline work.
        for (var pass = 0; pass < 8; pass++)
        {
            await processor.ProcessAsync(new(limit: 100, workflowExecutionId: origin.WorkerExecutionId));
            await DrainModelAsync(host, origin.WorkerExecutionId);
            if (await lookup.FindUndeliveredDurableAsync(origin.WorkerExecutionId, [BpmnNativeWorkBoundary.ResultKind]) is { } pending &&
                pending.AvailableAt > DateTimeOffset.UtcNow.AddMinutes(50)) break;
        }
        var delayed = (await lookup.FindUndeliveredDurableAsync(origin.WorkerExecutionId, [BpmnNativeWorkBoundary.ResultKind]))!;
        await CaptureAsync("waiting");
        Assert.NotNull(delayed);
        Assert.Equal(RuntimePostCommitIntentKinds.EnqueueSchedulerWork, delayed.Intent.Kind);
        Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, delayed.Intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!.CommandKind);
        Assert.True(delayed.AvailableAt > DateTimeOffset.UtcNow.AddMinutes(50));
        Assert.Equal(WorkflowExecutionStatus.Running,
            (await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(origin.WorkerExecutionId))!.Status);

        await runtime.CancelAsync(before.ExecutionId, "cancel-delayed-compensation", "Cancel during retry wait.", caller);
        await processor.ProcessAsync(new(limit: 100, workflowExecutionId: before.ExecutionId, intentKind: BpmnNativeWorkBoundary.CancelKind));
        await DrainModelAsync(host, origin.WorkerExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Cancelled,
            (await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(origin.WorkerExecutionId))!.Status);
        await processor.ProcessAsync(new(limit: 100, workflowExecutionId: origin.WorkerExecutionId, intentKind: BpmnNativeWorkBoundary.ResultKind));
        var originalInput = Assert.Single((await queue.ListAsync(new(before.ExecutionId))).Items);
        Assert.Equal(origin.ResultInputId, originalInput.CommandId);
        await CaptureAsync("queued-result");

        try { await DrainModelAsync(host, before.ExecutionId); }
        catch (RuntimeSchedulerWorkUnavailableException) { /* Preserve the failed run's actual state for the assertion/capture below. */ }
        var settled = (await reader.FindAsync(before.ExecutionId))!;
        await CaptureAsync("after-result");
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, settled.Lifecycle);
        Assert.Equal("Cancelled", settled.TerminalResult!.Value.GetProperty("status").GetString());
        Assert.Equal("Accepted", (await reader.FindInputAsync(before.ExecutionId, originalInput.CommandId))!.Result.GetProperty("status").GetString());
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await lookup.FindDurableAsync(delayed.OutboxItemId))!.Status);
        Assert.True(delayed.AvailableAt > DateTimeOffset.UtcNow.AddMinutes(50));
        Assert.Equal(before.Executable, settled.Executable);
        Assert.Equal(before.MaterialIncarnationId, settled.MaterialIncarnationId);
        Assert.True(WorkflowExecutionAuthoritySnapshot.Matches(settled.Authority,
            before.Authority.SystemIdentity, before.Authority.RootInitiator, before.Authority.Metadata));

        async Task CaptureAsync(string phase)
        {
            if (Environment.GetEnvironmentVariable("ELSA_BPMN_DELAYED_COMPENSATION_EVIDENCE") is not { Length: > 0 } root) return;
            Directory.CreateDirectory(root);
            await using var source = new SqliteConnection($"Data Source={database};Pooling=False");
            await source.OpenAsync();
            await using var destination = new SqliteConnection($"Data Source={Path.Combine(root, phase + ".sqlite")};Pooling=False");
            await destination.OpenAsync();
            source.BackupDatabase(destination);
            await File.WriteAllTextAsync(Path.Combine(root, phase + ".json"), JsonSerializer.Serialize(new
            {
                capturedAt = DateTimeOffset.UtcNow, model = before.ExecutionId, origin, delayed,
                state = await reader.FindAsync(before.ExecutionId),
                native = await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(origin.WorkerExecutionId),
                modelQueue = (await queue.ListAsync(new(before.ExecutionId))).Items
            }, Web));
        }
    }

    private static ExecutableNode DelayedCompensationWorker()
    {
        var forward = Leaf(typeof(WriteLine), "forward", "text", "Reserve before compensation.");
        var handler = Leaf(typeof(Fault), "handler");
        var fault = Leaf(typeof(Fault), "fault");
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(CompensationScope));
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal);
        Bind("faultAfterCompensation", false);
        Bind("handlerMaxAttempts", 3);
        Bind("confirmOnComplete", false);
        Bind("handlerRetryDelay", TimeSpan.FromHours(1));
        Bind("handlerRetryBackoffMultiplier", 2d);
        Bind("handlerRetryMaxDelay", TimeSpan.FromHours(1));
        var structure = new CompensationScopeExecutableStructure([
            new("reserve", forward.ExecutableNodeId, handler.ExecutableNodeId), new("fail", fault.ExecutableNodeId, null)]);
        return new("scope", "authored-scope", typeof(CompensationScope).FullName!, "1.0.0", contract.DescriptorKind,
            contract.DescriptorPayload, bindings,
            new Dictionary<string, string> { [RuntimeMetadataKeys.StructuralFaultHandling] = RuntimeMetadataKeys.StructuralFaultHandlingDescendant },
            [new(CompensationScope.StepSlotName("reserve"), [forward]), new(CompensationScope.CompensationSlotName("reserve"), [handler]),
                new(CompensationScope.StepSlotName("fail"), [fault])],
            structure: new(CompensationScope.StructureKind, CompensationScope.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, Web)),
            activityContract: contract);

        void Bind<T>(string key, T value)
        {
            var input = contract.Inputs[key];
            bindings[key] = new(key, input.Type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(input.Type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
        }
        static ExecutableNode Leaf(Type type, string id, string? inputKey = null, string? value = null)
        {
            var leafContract = ClrActivityContractTestBuilder.BuildContract(type);
            var inputs = new Dictionary<string, RuntimeInputBinding>();
            if (inputKey is not null)
            {
                var input = leafContract.Inputs[inputKey];
                inputs[inputKey] = new(inputKey, input.Type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(input.Type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
            }
            return new(id, "authored-" + id, type.FullName!, "1.0.0", leafContract.DescriptorKind, leafContract.DescriptorPayload,
                ClrActivityContractTestBuilder.CompleteInputBindings(leafContract, inputs),
                new Dictionary<string, string>(), activityContract: leafContract);
        }
    }
}
