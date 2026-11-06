using System.Text.Json;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Fact]
    public async Task Native_consumer_keeps_logical_process_identity_across_distinct_material_versions()
    {
        await using var host = await OpenAsync();
        var worker = ConsumerWorker();
        await host.PublishAsync(worker, "consumer-worker");
        await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        var runtime = services.GetRequiredService<IBpmnModelRuntime>();
        var consumer = services.GetRequiredService<IBpmnNativeWorkConsumer>();
        var caller = new BpmnModelCaller("operator", null);
        var first = await runtime.PublishAsync(ConsumerDefinition(worker.Identity, "first-end"), "first-material", caller);
        var second = await runtime.PublishAsync(ConsumerDefinition(worker.Identity, "second-end"), "second-material", caller);
        Assert.NotEqual(first.Executable.ArtifactId, second.Executable.ArtifactId);
        var addresses = new List<BpmnNativeWorkAddress>();
        foreach (var (published, id) in new[] { (first, "first-start"), (second, "second-start") })
        {
            var start = await runtime.StartAsync(published.SourceReferenceId, id, null, caller);
            var state = (await runtime.FindExecutionAsync(start.ExecutionId, caller))!;
            var origin = Assert.Single(BpmnDirectExecutionDriver.Read(state).Work).NativeWork!;
            var address = await consumer.ResolveAsync(origin);
            Assert.Equal("orders", address.LogicalDefinitionId);
            Assert.Equal(published.Executable, address.Origin.Executable);
            Assert.Equal(published.IncarnationId, address.Origin.MaterialIncarnationId);
            Assert.NotEqual(address.LogicalDefinitionId, address.Origin.Executable.DefinitionId);
            Assert.Equal(worker.RootActivity.ActivityContract!.SchemaFingerprint, address.WorkerRootContract.SchemaFingerprint);
            addresses.Add(address);
        }
        Assert.Equal(addresses[0].LogicalDefinitionId, addresses[1].LogicalDefinitionId);
        Assert.NotEqual(addresses[0].Origin.ExecutionId, addresses[1].Origin.ExecutionId);
    }

    [Fact]
    public async Task Native_consumer_requires_actual_accepted_root_input_after_delivered_start_and_completed_helper()
    {
        await using var host = await OpenAsync(activityIds: ["consumer-native-root"]);
        var worker = ConsumerWorker();
        await host.PublishAsync(worker, "consumer-worker");
        await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        var runtime = services.GetRequiredService<IBpmnModelRuntime>();
        var consumer = services.GetRequiredService<IBpmnNativeWorkConsumer>();
        var caller = new BpmnModelCaller("operator", null);
        var published = await runtime.PublishAsync(ConsumerDefinition(worker.Identity, "end"), "consumer-material", caller);
        var start = await runtime.StartAsync(published.SourceReferenceId, "consumer-start", null, caller);
        var before = (await runtime.FindExecutionAsync(start.ExecutionId, caller))!;
        var origin = Assert.Single(BpmnDirectExecutionDriver.Read(before).Work).NativeWork!;
        var processor = services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        await processor.ProcessAsync(new(limit: 100, workflowExecutionId: before.ExecutionId, intentKind: BpmnNativeWorkBoundary.StartKind));
        var source = (await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(origin.SourceOutboxItemId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, source.Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(origin.WorkerExecutionId))!.Status);
        Assert.Equal("orders", (await consumer.ResolveAsync(origin)).LogicalDefinitionId);
        Assert.Equal(BpmnNativeWorkObservationKind.NotConsumed, (await consumer.InspectAsync(origin)).Kind);
        await processor.ProcessAsync(new(limit: 100, workflowExecutionId: origin.WorkerExecutionId, intentKind: BpmnNativeWorkBoundary.ResultKind));
        Assert.Equal(BpmnNativeWorkObservationKind.NotConsumed, (await consumer.InspectAsync(origin)).Kind);
        Assert.Null(await services.GetRequiredService<IRuntimeModelExecutionReader>().FindInputAsync(before.ExecutionId, origin.ResultInputId));
        await DrainModelAsync(host, before.ExecutionId);
        var proof = await consumer.InspectAsync(origin);
        Assert.Equal(BpmnNativeWorkObservationKind.AcceptedRootInput, proof.Kind);
        Assert.Equal(origin.ResultInputId, proof.AcceptedInput!.InputId);
        Assert.Equal(WorkflowExecutionStatus.Completed, proof.AcceptedInput.Result.Status);
        Assert.Equal(worker.RootActivity.ActivityContract!.SchemaFingerprint, proof.AcceptedInput.Result.Completion!.ContractFingerprint);
        Assert.NotNull(proof.ModelReceiptId);
        var receipt = (await services.GetRequiredService<IRuntimeModelExecutionReader>().FindInputAsync(before.ExecutionId, origin.ResultInputId))!;
        Assert.Equal(receipt.ContentFingerprint, proof.AcceptedInput.ContentFingerprint);
        Assert.Equal(receipt.AcceptedRevision, proof.AcceptedInput.AcceptedRevision);
        Assert.Equal(receipt.AcceptedAt, proof.AcceptedInput.AcceptedAt);
    }

    [Fact]
    public async Task Native_result_contribution_refuses_before_live_progression_and_is_not_repeated_for_exact_input_replay()
    {
        var validator = new ConsumerValidationGate();
        await using var host = await OpenAsync(configure: registrations => registrations.AddSingleton<IBpmnNativeWorkResultValidator>(validator),
            activityIds: ["consumer-native-root"]);
        var worker = ConsumerWorker();
        await host.PublishAsync(worker, "validator-worker");
        await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        var runtime = services.GetRequiredService<IBpmnModelRuntime>();
        var reader = services.GetRequiredService<IRuntimeModelExecutionReader>();
        var caller = new BpmnModelCaller("operator", null);
        var published = await runtime.PublishAsync(ConsumerDefinition(worker.Identity, "end"), "validator-material", caller);
        var start = await runtime.StartAsync(published.SourceReferenceId, "validator-start", null, caller);
        var before = (await runtime.FindExecutionAsync(start.ExecutionId, caller))!;
        var origin = Assert.Single(BpmnDirectExecutionDriver.Read(before).Work).NativeWork!;
        var processor = services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        await processor.ProcessAsync(new(limit: 100, workflowExecutionId: before.ExecutionId, intentKind: BpmnNativeWorkBoundary.StartKind));
        var source = (await services.GetRequiredService<IPostCommitOutboxLookupStore>()
            .FindUndeliveredDurableAsync(origin.WorkerExecutionId, [RuntimePostCommitIntentKinds.EnqueueSchedulerWork]))!;
        Assert.Equal(BpmnNativeWorkBoundary.ResultKind, source.Intent.Kind);
        var material = (await services.GetRequiredService<IWorkflowExecutableStore>().FindMaterialAsync(before.Executable.ArtifactId))!;
        var command = new RuntimeModelExecutionCommand(before.Executable, before.MaterialIncarnationId, origin.ResultInputId,
            "native-result", source.Intent.Payload!.Value, before.Authority, before.TenantId);
        var at = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var item = new RuntimeSchedulerWorkItem("consumer-result", before.ExecutionId, command.InputId,
            WorkflowExecutionCommandKind.ExecuteModel, "consumer-result", "consumer-result", at, at);
        var driver = services.GetRequiredService<BpmnDirectExecutionDriver>();
        validator.Refuse = true;
        var refusal = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => driver.ExecuteAsync(
            new(before.Scope, material, before, command, item)).AsTask());
        Assert.Equal("test.native-result-held", refusal.Code);
        Assert.Equal(1, validator.Calls);
        Assert.Null(await reader.FindInputAsync(before.ExecutionId, origin.ResultInputId));
        Assert.Equal(before.Revision, (await reader.FindAsync(before.ExecutionId))!.Revision);
        Assert.Empty(BpmnDirectExecutionDriver.Read((await reader.FindAsync(before.ExecutionId))!).NativeResults ?? []);
        validator.Refuse = false;
        await processor.ProcessAsync(new(limit: 100, workflowExecutionId: origin.WorkerExecutionId, intentKind: BpmnNativeWorkBoundary.ResultKind));
        await DrainModelAsync(host, before.ExecutionId);
        Assert.Equal(2, validator.Calls);
        var accepted = (await reader.FindAsync(before.ExecutionId))!;
        Assert.Equal("Accepted", (await reader.FindInputAsync(before.ExecutionId, origin.ResultInputId))!.Result.GetProperty("status").GetString());
        validator.Refuse = true;
        await driver.ExecuteAsync(new(before.Scope, material, accepted, command, item));
        Assert.Equal(2, validator.Calls);
        Assert.Equal(accepted.Revision, (await reader.FindAsync(before.ExecutionId))!.Revision);
    }

    [Fact]
    public async Task Native_result_delivery_keeps_the_existing_source_owed_until_its_contribution_confirms_settlement()
    {
        var gate = new ConsumerDeliveryGate();
        var clock = new DurationClock(DateTimeOffset.Parse("2026-11-06T00:00:00Z"));
        await using var host = await OpenAsync(configure: registrations =>
        {
            registrations.AddSingleton<IBpmnNativeWorkResultDeliveryContributor>(gate);
            registrations.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
        }, activityIds: ["consumer-native-root"]);
        var worker = ConsumerWorker();
        await host.PublishAsync(worker, "delivery-worker");
        await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        var runtime = services.GetRequiredService<IBpmnModelRuntime>();
        var reader = services.GetRequiredService<IRuntimeModelExecutionReader>();
        var lookup = services.GetRequiredService<IPostCommitOutboxLookupStore>();
        var caller = new BpmnModelCaller("operator", null);
        var published = await runtime.PublishAsync(ConsumerDefinition(worker.Identity, "end"), "delivery-material", caller);
        var started = await runtime.StartAsync(published.SourceReferenceId, "delivery-start", null, caller);
        var before = (await reader.FindAsync(started.ExecutionId))!;
        var origin = Assert.Single(BpmnDirectExecutionDriver.Read(before).Work).NativeWork!;
        var processor = services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        await processor.ProcessAsync(new(100, before.ExecutionId, BpmnNativeWorkBoundary.StartKind));
        var resultSource = (await lookup.FindUndeliveredDurableAsync(origin.WorkerExecutionId,
            [RuntimePostCommitIntentKinds.EnqueueSchedulerWork]))!;
        Assert.Equal(BpmnNativeWorkBoundary.ResultKind, resultSource.Intent.Kind);
        await processor.ProcessAsync(new(100, origin.WorkerExecutionId, BpmnNativeWorkBoundary.ResultKind));
        Assert.Equal(0, gate.Calls);
        await DrainModelAsync(host, before.ExecutionId);
        var accepted = (await reader.FindAsync(before.ExecutionId))!;
        var input = (await reader.FindInputAsync(before.ExecutionId, origin.ResultInputId))!;
        Assert.Equal("Accepted", input.Result.GetProperty("status").GetString());
        clock.Advance(TimeSpan.FromSeconds(2));
        var held = await processor.ProcessAsync(new(100, origin.WorkerExecutionId, BpmnNativeWorkBoundary.ResultKind));
        Assert.Equal(1, held.FailedCount);
        Assert.Equal(1, gate.Calls);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, (await lookup.FindDurableAsync(resultSource.OutboxItemId))!.Status);
        Assert.Equal(BpmnNativeWorkInputDisposition.Accepted, gate.Context!.Disposition);
        Assert.Equal(input.ContentFingerprint, gate.Context.InputReceipt.ContentFingerprint);
        Assert.NotNull(gate.Context.TerminalReceiptId);
        gate.Refuse = false;
        clock.Advance(TimeSpan.FromSeconds(2));
        var settled = await processor.ProcessAsync(new(100, origin.WorkerExecutionId, BpmnNativeWorkBoundary.ResultKind));
        Assert.Equal(1, settled.DeliveredCount);
        Assert.Equal(2, gate.Calls);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await lookup.FindDurableAsync(resultSource.OutboxItemId))!.Status);
        Assert.Equal(accepted.Revision, (await reader.FindAsync(before.ExecutionId))!.Revision);
        Assert.Equal(input.ContentFingerprint, (await reader.FindInputAsync(before.ExecutionId, origin.ResultInputId))!.ContentFingerprint);
    }

    [Theory]
    [InlineData("missing-owner")]
    [InlineData("rejected-input")]
    [InlineData("wrong-fingerprint")]
    public async Task Native_consumer_never_promotes_missing_or_rejected_or_mismatched_evidence_to_root_consumption(string variant)
    {
        await using var host = await OpenAsync(activityIds: ["consumer-native-root"]);
        var worker = ConsumerWorker();
        await host.PublishAsync(worker, "consumer-worker");
        await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        var runtime = services.GetRequiredService<IBpmnModelRuntime>();
        var reader = services.GetRequiredService<IRuntimeModelExecutionReader>();
        var caller = new BpmnModelCaller("operator", null);
        var published = await runtime.PublishAsync(ConsumerDefinition(worker.Identity, "end", concurrentServices: true), "consumer-material", caller);
        var started = await runtime.StartAsync(published.SourceReferenceId, "consumer-start", null, caller);
        var before = (await runtime.FindExecutionAsync(started.ExecutionId, caller))!;
        var origins = BpmnDirectExecutionDriver.Read(before).Work.Select(item => item.NativeWork!).ToArray();
        Assert.Equal(2, origins.Length);
        var processor = services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        await processor.ProcessAsync(new(limit: 1, workflowExecutionId: before.ExecutionId, intentKind: BpmnNativeWorkBoundary.StartKind));
        var completedOrigins = new List<RuntimeModelNativeWorkOrigin>();
        foreach (var candidate in origins)
            if (await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(candidate.WorkerExecutionId) is { Status: WorkflowExecutionStatus.Completed })
                completedOrigins.Add(candidate);
        var origin = Assert.Single(completedOrigins);
        await processor.ProcessAsync(new(limit: 100, workflowExecutionId: origin.WorkerExecutionId, intentKind: BpmnNativeWorkBoundary.ResultKind));
        await DrainModelAsync(host, before.ExecutionId);
        var current = (await reader.FindAsync(before.ExecutionId))!;
        Assert.Equal(RuntimeModelLifecycle.Running, current.Lifecycle);
        var original = (await reader.FindInputAsync(before.ExecutionId, origin.ResultInputId))!;
        Assert.Equal("Accepted", original.Result.GetProperty("status").GetString());
        var observationReader = DeliveryFake<IRuntimeModelExecutionReader>((method, _) => method.Name switch
        {
            "FindAsync" => ValueTask.FromResult<RuntimeModelExecutionState?>(variant == "missing-owner" ? null : current),
            "FindInputAsync" => ValueTask.FromResult<RuntimeModelInputReceipt?>(new(original.Scope, original.ExecutionId, original.InputId,
                original.InputKind, variant == "wrong-fingerprint" ? new string('f', 64) : original.ContentFingerprint,
                original.AcceptedRevision, original.AcceptedAt, original.InvocationId, original.AttemptId,
                variant == "rejected-input" ? JsonSerializer.SerializeToElement(new { status = "Rejected" }) : original.Result)),
            _ => throw new InvalidOperationException(method.Name)
        });
        var consumer = new BpmnNativeWorkConsumer(services.GetRequiredService<BpmnNativeWorkBoundary>(),
            services.GetRequiredService<IWorkflowExecutableStore>(), services.GetRequiredService<IPersistenceAccessContextAccessor>(), observationReader);
        if (variant == "missing-owner")
            await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => consumer.InspectAsync(origin).AsTask());
        else if (variant == "wrong-fingerprint")
            await Assert.ThrowsAsync<BpmnModelValidationException>(() => consumer.InspectAsync(origin).AsTask());
        else
        {
            var proof = await consumer.InspectAsync(origin);
            Assert.Equal(BpmnNativeWorkObservationKind.NotConsumed, proof.Kind);
            Assert.Null(proof.AcceptedInput);
            Assert.Null(proof.TerminalReceiptId);
        }
        Assert.Equal(current.Revision, (await reader.FindAsync(before.ExecutionId))!.Revision);
        Assert.Equal(original.ContentFingerprint, (await reader.FindInputAsync(before.ExecutionId, origin.ResultInputId))!.ContentFingerprint);
    }

    private static WorkflowExecutable ConsumerWorker() => new(WorkflowExecutionHarness.Identity,
        WorkflowExecutionHarness.NewProbeNode("consumer-native-root"), new Dictionary<string, WorkflowExecutableResumeTarget>(),
        WorkflowExecutionHarness.Timestamp, new Dictionary<string, string>(), inputContract: new(1, []), dependencies: null,
        incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    private static BpmnModelDefinition ConsumerDefinition(WorkflowExecutableIdentity worker, string end, bool concurrentServices = false)
    {
        var elements = new List<BpmnElement>
            { new("start", BpmnElementTypes.StartEvent), new("service", BpmnElementTypes.ServiceTask, bindingRef: "worker"), new(end, BpmnElementTypes.EndEvent) };
        var flows = new List<BpmnSequenceFlow> { new("begin", "start", "service") };
        if (concurrentServices) flows.Add(new("concurrent", "start", "service"));
        flows.Add(new("finish", "service", end));
        var process = new BpmnProcessDefinition("orders", Elements:
            elements, SequenceFlows: flows);
        return new(concurrentServices ? 2 : 3, JsonSerializer.SerializeToElement(process, Web), [],
            [new("worker", worker, "worker.business-error", concurrentServices ? null : new(1, BpmnServiceFaultDisposition.FinalFault, 1))]);
    }

    private sealed class ConsumerValidationGate : IBpmnNativeWorkResultValidator
    {
        public bool Refuse { get; set; }
        public int Calls { get; private set; }
        public ValueTask ValidateAsync(BpmnNativeWorkResultValidation context, CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.Equal(context.Origin.ResultInputId, context.InputId);
            Assert.True(context.Origin.IsEquivalentTo(context.Worker.ModelWorkOrigin));
            if (Refuse) throw new RuntimeSchedulerWorkUnavailableException("test.native-result-held", "The test result permission remains held.");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ConsumerDeliveryGate : IBpmnNativeWorkResultDeliveryContributor
    {
        internal bool Refuse = true;
        internal int Calls;
        internal BpmnNativeWorkResultDelivery? Context;
        public ValueTask CompleteAsync(BpmnNativeWorkResultDelivery context, CancellationToken cancellationToken = default)
        {
            Calls++; Context = context;
            Assert.True(context.Origin.IsEquivalentTo(context.Worker.ModelWorkOrigin));
            Assert.Equal(context.Origin.ResultInputId, context.InputReceipt.InputId);
            if (Refuse) throw new RuntimePostCommitDeliveryException(PostCommitFailureKind.Transient,
                "test.native-settlement-held", "The owned test settlement remains owed.");
            return ValueTask.CompletedTask;
        }
    }
}
