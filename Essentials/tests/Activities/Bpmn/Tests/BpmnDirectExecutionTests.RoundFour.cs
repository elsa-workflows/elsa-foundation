using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Theory]
    [InlineData(BpmnDurationTimer.Kind)]
    [InlineData(BpmnNativeWorkBoundary.StartKind)]
    [InlineData(BpmnNativeWorkBoundary.CancelKind)]
    [InlineData(BpmnNativeWorkBoundary.ResultKind)]
    [InlineData(BpmnEmbeddingBoundary.StartKind)]
    [InlineData(BpmnEmbeddingBoundary.CancelKind)]
    [InlineData(BpmnEmbeddingBoundary.ResultKind)]
    public async Task Malformed_owned_delivery_JSON_preserves_execution_source_kind_and_cause(string kind)
    {
        await using var host = await OpenAsync();
        var scope = new PersistenceScope(PersistenceScope.DefaultValue);
        var now = DateTimeOffset.UtcNow;
        var intent = new RuntimePostCommitIntent("reviewed-intent", "reviewed-execution", kind, now, null, null,
            JsonSerializer.SerializeToElement("invalid-owned-address"));
        var current = new RuntimePostCommitOutboxItem("reviewed-commit:reviewed-intent", intent,
            RuntimePostCommitOutboxStatus.Delivering, now, now, RuntimePostCommitRetryPolicy.None, 1, "delivery-owner",
            now, null, null, null, 1, now.AddMinutes(5), 1, scope, null);
        var source = new PostCommitDeliveryContext(new RuntimePostCommitOutboxClaim(current, "delivery-owner", 1,
            now, now.AddMinutes(5)), scope);
        var outbox = DeliveryFake<DeliveryOutbox>((method, _) => method.Name == "FindDurableAsync"
            ? ValueTask.FromResult<RuntimePostCommitOutboxItem?>(current) : throw new InvalidOperationException(method.Name));
        var access = DeliveryFake<IPersistenceAccessContextAccessor>((method, _) => method.Name == "get_Current"
            ? PersistenceAccessContext.Scoped(scope) : throw new InvalidOperationException(method.Name));
        var checkpoints = DeliveryFake<IRuntimeCheckpointCommitStore>((method, _) => method.Name is
            "get_SupportsModelAdmission" or "CanCommitModelAdmission" or "get_SupportsModelParentAdmission" or "CanCommitExactBookmarkResume"
            ? true : throw new InvalidOperationException(method.Name));
        var materials = DeliveryFake<IWorkflowExecutableStore>();
        var reader = DeliveryFake<IRuntimeModelExecutionReader>();
        var queue = DeliveryFake<IWorkflowSchedulerWorkQueue>();
        var native = new BpmnNativeWorkBoundary(materials, outbox, DeliveryFake<IWorkflowExecutionStateStore>(),
            DeliveryFake<IActivityExecutionStateStore>(), checkpoints, outbox, access, TimeProvider.System, queue, reader);
        var actors = host.Services.GetRequiredService<IWorkflowExecutionActorProvider>();
        IRuntimePostCommitDurableSourceIntentHandler handler = kind == BpmnDurationTimer.Kind
            ? new BpmnDurationTimerDelivery(native, outbox, queue, access, actors, TimeProvider.System, reader)
            : kind is BpmnNativeWorkBoundary.StartKind or BpmnNativeWorkBoundary.CancelKind or BpmnNativeWorkBoundary.ResultKind
                ? new BpmnNativeWorkDelivery(native, DeliveryFake<IWorkflowStartDispatcher>(), queue, actors, TimeProvider.System, reader)
                : new BpmnEmbeddingDelivery(new(native, materials, DeliveryFake<IBookmarkStateStore>(), checkpoints,
                    access, TimeProvider.System, new(false)), native, queue, DeliveryFake<IBookmarkResumeDispatcher>(),
                    actors, TimeProvider.System, reader);
        using var provider = new ServiceCollection().AddSingleton(handler.GetType(), handler).BuildServiceProvider();
        var dispatcher = new RuntimePostCommitIntentDispatcher([new(kind, handler.GetType(),
            RuntimePostCommitRetryPolicy.None, requiresDurableSource: true)], provider);
        var error = await Assert.ThrowsAsync<BpmnModelValidationException>(() => dispatcher.DispatchAsync(source).AsTask());
        Assert.IsAssignableFrom<JsonException>(error.InnerException);
        Assert.Contains(intent.WorkflowExecutionId, error.Message);
        Assert.Contains(source.SourceOutboxItemId, error.Message);
        Assert.Contains(kind, error.Message);
    }

    private static T DeliveryFake<T>(Func<MethodInfo, object?[]?, object?>? callback = null) where T : class
    {
        var value = DispatchProxy.Create<T, DeliveryProxy>();
        ((DeliveryProxy)(object)value).Callback = callback ?? ((method, _) => throw new InvalidOperationException(method.Name));
        return value;
    }

    public interface DeliveryOutbox : IPostCommitOutboxLookupStore, IRuntimePostCommitOutboxStore;
    public class DeliveryProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Callback = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Callback(targetMethod!, args);
    }
}
