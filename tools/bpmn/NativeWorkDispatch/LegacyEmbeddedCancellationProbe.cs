using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Bpmn;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

internal static class LegacyEmbeddedCancellationProbe
{
    public static async Task RunAsync(IServiceProvider sdk, RuntimeModelExecutionState before, JsonElement owner, string operation, JsonSerializerOptions json)
    {
        if (before.Payload.GetProperty("formatVersion").GetInt32() != 5 ||
            before.TerminalResult?.GetProperty("status").GetString() != "Faulted" ||
            before.Payload.GetProperty("work").GetArrayLength() != 1 ||
            before.Payload.TryGetProperty("cancellations", out var cancellations) && cancellations.ValueKind != JsonValueKind.Null && cancellations.GetArrayLength() != 0)
            throw new InvalidOperationException("The probe requires the unchanged actual old format5 fault and retained native work.");
        var invocation = before.Payload.GetProperty("nativeParent").GetProperty("invocation");
        var parentId = owner.GetProperty("nativeParentExecutionId").GetString()!;
        var parentBytes = new byte[parentId.Length * 2];
        for (var index = 0; index < parentId.Length; index++)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(parentBytes.AsSpan(index * 2, 2), parentId[index]);
        if (invocation.GetProperty("nativeExecutionId").GetString() != "u16:" + Convert.ToBase64String(parentBytes))
            throw new InvalidOperationException("The owned marker must identify the exact retained native parent.");
        var origin = before.Payload.GetProperty("work")[0].GetProperty("nativeWork").Deserialize<RuntimeModelNativeWorkOrigin>(json)!;
        var executions = sdk.GetRequiredService<IWorkflowExecutionStateStore>();
        var models = sdk.GetRequiredService<IRuntimeModelExecutionReader>();
        var queue = sdk.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var outbox = sdk.GetRequiredService<IRuntimePostCommitOutboxStore>();
        var parentBefore = await executions.FindAsync(parentId) ?? throw new InvalidOperationException("The actual native parent is absent.");
        var workerBefore = await executions.FindAsync(origin.WorkerExecutionId) ?? throw new InvalidOperationException("The original native worker is absent.");
        if (workerBefore.Status != WorkflowExecutionStatus.Running || !origin.MatchesModel(before) || !origin.IsEquivalentTo(workerBefore.ModelWorkOrigin))
            throw new InvalidOperationException("The actual outstanding native worker must still be Running.");
        var now = DateTimeOffset.UtcNow;
        object? dispatch = null;
        RuntimePostCommitOutboxItem? addressedSource = null;
        if (operation is "--enqueue-legacy-parent-cancel" or "--drain-legacy-parent-cancel")
        {
            if (parentBefore.Status != WorkflowExecutionStatus.Running)
                throw new InvalidOperationException("The captured native parent must still be Running before its ordinary cancellation command.");
            var ids = sdk.GetRequiredService<IRuntimeExecutionIdGenerator>();
            var metadata = new Dictionary<string, string> { ["runtime.cancellationSource"] = "owned-legacy-upgrade" };
            var kind = operation == "--enqueue-legacy-parent-cancel" ? WorkflowExecutionCommandKind.Cancel : WorkflowExecutionCommandKind.RunSchedulerWork;
            var command = new WorkflowExecutionCommand(ids.NewWorkflowExecutionCommandId(), parentId, kind, now, Payload: null, Metadata: metadata);
            var partition = new WorkflowExecutionPartition(before.Scope.Value);
            var actor = await sdk.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(parentId,
                WorkflowExecutionActorActivationReason.ControlPlaneCommand, now, "owned-legacy-upgrade",
                WorkflowExecutionActorCapabilities.InProcessMailbox, metadata, partition));
            dispatch = await actor.EnqueueAsync(new(ids.NewWorkflowExecutionCommandEnvelopeId(), parentId, command,
                "owned-legacy-upgrade:" + command.CommandId, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now,
                metadata: metadata, partition: partition));
        }
        else
        {
            addressedSource = (await outbox.GetDeliverableAsync(new(now, 10, parentId, "Elsa.Bpmn.CancelEmbedded.v1"))).Single();
            dispatch = await sdk.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(1, parentId, "Elsa.Bpmn.CancelEmbedded.v1"));
            addressedSource = await sdk.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(addressedSource.OutboxItemId);
        }
        var after = await models.FindAsync(before.ExecutionId);
        if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(before, json), JsonSerializer.SerializeToElement(after, json)))
            throw new InvalidOperationException("The owned transport step must leave the original model input to its later normal mailbox drain.");
        object Product(Assembly assembly) => new { name = assembly.GetName().Name,
            informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))) };
        Console.WriteLine(JsonSerializer.Serialize(new { operation, startedAtUtc = now, completedAtUtc = DateTimeOffset.UtcNow,
            executionStateSeeded = false, dispatch, modelBefore = before, modelAfter = after, parentBefore,
            parentAfter = await executions.FindAsync(parentId), workerBefore, workerAfter = await executions.FindAsync(origin.WorkerExecutionId),
            queuedParentWork = (await queue.ListAsync(new(parentId))).Items,
            queuedModelInputs = (await queue.ListAsync(new(before.ExecutionId))).Items, addressedSource,
            pendingCancelSources = await outbox.GetDeliverableAsync(new(DateTimeOffset.UtcNow, 10, parentId, "Elsa.Bpmn.CancelEmbedded.v1")),
            products = new[] { Product(Assembly.GetExecutingAssembly()), Product(typeof(IWorkflowStartDispatcher).Assembly),
                Product(typeof(ActivitiesBpmnHostingFeature).Assembly) } }, json));
    }
}
