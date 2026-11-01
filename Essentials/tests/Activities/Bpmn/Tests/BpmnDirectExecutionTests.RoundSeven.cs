using System.Text.Json;
using Bpmn.Model.State;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Embedded_delivery_replays_start_but_keeps_cancel_owed_until_native_cancellation_settles(bool start, bool settled)
    {
        var scope = new PersistenceScope(PersistenceScope.DefaultValue);
        var now = DateTimeOffset.UtcNow;
        var modelPin = new WorkflowExecutableIdentity("model-artifact", "model-definition", "model-version", "1.0.0", "model-hash");
        var nativePin = new WorkflowExecutableIdentity("native-artifact", "native-definition", "native-version", "1.0.0", "native-hash");
        var childPin = new WorkflowExecutableIdentity("child-artifact", "child-definition", "child-version", "1.0.0", "child-hash");
        var execution = "bpmn-embedded:" + BpmnModelIdentity.Create(scope.Value, "outer-native-worker", "runbpmn-invocation");
        var invocation = new BpmnEmbeddedInvocation(1, scope, execution, new("published-model-source", modelPin, "incarnation"), nativePin,
            "outer-native-worker", "runbpmn-invocation", "runbpmn-node", "resume-target", "registration", "attempt");
        var protocol = new RuntimeExecutionProtocol(BpmnModelProtocol.Key, BpmnModelProtocol.Version);
        var origin = new RuntimeModelNativeWorkOrigin(1, scope, execution, protocol, modelPin, "incarnation", "worker", "child-invocation", "child-attempt", childPin,
            BpmnNativeWorkBoundary.WorkerIdentity(scope, execution, "child-invocation", "child-attempt", childPin).WorkflowExecutionId,
            "start-commit:child-start", "child-start", BpmnNativeWorkBoundary.ResultKind, "child-result");
        var cancellation = new BpmnNativeWorkCancellation(origin, "cancel-commit:" + BpmnNativeWorkBoundary.CancellationIntentId(origin), Settled: settled);
        var document = new BpmnDirectExecutionDriver.Document(6, new BpmnExecutionState(), [],
            NativeParent: new(BpmnEmbeddingJson.Write(invocation)), Cancellations: [cancellation],
            NativeResults: settled ? [new(origin, "child-terminal:result", WorkflowExecutionStatus.Cancelled, null, null, [])] : null);
        var model = new RuntimeModelExecutionState(1, scope, execution, null, protocol, modelPin, "incarnation", 2,
            RuntimeModelLifecycle.TerminalPendingSettlement, new("system", "operator"), now, now,
            JsonSerializer.SerializeToElement(document, Web), JsonSerializer.SerializeToElement(new { status = "Cancelled" }));
        Assert.Equal(settled, Assert.Single(BpmnDirectExecutionDriver.Read(model).Cancellations!).Settled);
        var intent = BpmnEmbeddingCheckpointEnricher.Intent(invocation, !start, now);
        var current = new RuntimePostCommitOutboxItem("parent-commit:" + intent.IntentId, intent,
            RuntimePostCommitOutboxStatus.Delivering, now, now, RuntimePostCommitRetryPolicy.None, 1, "delivery-owner", now,
            null, null, null, 1, now.AddMinutes(5), 1, scope, null);
        var source = new PostCommitDeliveryContext(new RuntimePostCommitOutboxClaim(current, "delivery-owner", 1, now, now.AddMinutes(5)), scope);
        var outbox = DeliveryFake<DeliveryOutbox>((method, _) => method.Name == "FindDurableAsync"
            ? ValueTask.FromResult<RuntimePostCommitOutboxItem?>(current) : throw new InvalidOperationException(method.Name));
        var access = DeliveryFake<IPersistenceAccessContextAccessor>((method, _) => method.Name == "get_Current"
            ? PersistenceAccessContext.Scoped(scope) : throw new InvalidOperationException(method.Name));
        var checkpoints = DeliveryFake<IRuntimeCheckpointCommitStore>((method, _) => method.Name is
            "get_SupportsModelAdmission" or "CanCommitModelAdmission" or "get_SupportsModelParentAdmission" or "CanCommitExactBookmarkResume"
            ? true : throw new InvalidOperationException(method.Name));
        var reader = DeliveryFake<IRuntimeModelExecutionReader>((method, _) => method.Name == "FindAsync"
            ? ValueTask.FromResult<RuntimeModelExecutionState?>(model) : throw new InvalidOperationException(method.Name));
        var materials = DeliveryFake<IWorkflowExecutableStore>();
        var queue = DeliveryFake<IWorkflowSchedulerWorkQueue>();
        var native = new BpmnNativeWorkBoundary(materials, outbox, DeliveryFake<IWorkflowExecutionStateStore>(),
            DeliveryFake<IActivityExecutionStateStore>(), checkpoints, outbox, access, TimeProvider.System, queue, reader);
        var boundary = new BpmnEmbeddingBoundary(native, materials, DeliveryFake<IBookmarkStateStore>(), checkpoints,
            access, TimeProvider.System, new(false), models: reader);
        using var actors = new InProcessWorkflowExecutionActorProvider();
        var delivery = new BpmnEmbeddingDelivery(boundary, native, queue, DeliveryFake<IBookmarkResumeDispatcher>(), actors, TimeProvider.System, reader);

        if (!start && !settled)
            await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => delivery.HandleAsync(source).AsTask());
        else
            await delivery.HandleAsync(source);
    }
}
