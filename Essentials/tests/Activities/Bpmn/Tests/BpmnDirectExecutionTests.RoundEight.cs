using System.Text.Json;
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_old_fault_work_keeps_embedded_cancel_owed_without_blocking_start_replay(bool start)
    {
        var model = JsonSerializer.Deserialize<RuntimeModelExecutionState>(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "ActualLegacyPendingParentFaultModel.json")), Web)!;
        var document = BpmnDirectExecutionDriver.Read(model);
        Assert.Equal(5, document.FormatVersion);
        Assert.Single(document.Work.Where(item => item.NativeWork is not null));
        Assert.Null(document.Cancellations);
        var invocation = BpmnEmbeddingBoundary.Read(new RunBpmnState(1, document.NativeParent!.Invocation));
        var now = DateTimeOffset.UtcNow;
        var intent = BpmnEmbeddingCheckpointEnricher.Intent(invocation, !start, now);
        var current = new RuntimePostCommitOutboxItem("parent-cancel:" + intent.IntentId, intent,
            RuntimePostCommitOutboxStatus.Delivering, now, now, RuntimePostCommitRetryPolicy.None, 1, "delivery-owner", now,
            null, null, null, 1, now.AddMinutes(5), 1, model.Scope, null);
        var source = new PostCommitDeliveryContext(new RuntimePostCommitOutboxClaim(current, "delivery-owner", 1, now, now.AddMinutes(5)), model.Scope);
        var outbox = DeliveryFake<DeliveryOutbox>((method, _) => method.Name == "FindDurableAsync"
            ? ValueTask.FromResult<RuntimePostCommitOutboxItem?>(current) : throw new InvalidOperationException(method.Name));
        var access = DeliveryFake<IPersistenceAccessContextAccessor>((method, _) => method.Name == "get_Current"
            ? PersistenceAccessContext.Scoped(model.Scope) : throw new InvalidOperationException(method.Name));
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
        if (start) await delivery.HandleAsync(source);
        else await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => delivery.HandleAsync(source).AsTask());
    }
}
