using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Bpmn;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Transports actual old result bytes, then explicitly modified negative inputs; never seeds execution state.</summary>
internal static class ObsoleteResultProbe
{
    internal static async Task RunAsync(IServiceProvider sdk, RuntimeModelExecutionState before, JsonSerializerOptions json)
    {
        var started = DateTimeOffset.UtcNow;
        var reader = sdk.GetRequiredService<IRuntimeModelExecutionReader>();
        var workflows = sdk.GetRequiredService<IWorkflowExecutionStateStore>();
        var sources = sdk.GetRequiredService<IPostCommitOutboxLookupStore>();
        var queue = sdk.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var old = before.Payload.GetProperty("nativeResults").EnumerateArray().Single().Clone();
        var oldOrigin = old.GetProperty("origin").Deserialize<RuntimeModelNativeWorkOrigin>(json)!;
        var current = before.Payload.GetProperty("work").EnumerateArray().Single().GetProperty("nativeWork").Deserialize<RuntimeModelNativeWorkOrigin>(json)!;
        var oldWorker = await workflows.FindAsync(oldOrigin.WorkerExecutionId);
        var currentWorker = await workflows.FindAsync(current.WorkerExecutionId);
        var originalReceipt = await reader.FindInputAsync(before.ExecutionId, oldOrigin.ResultInputId);
        var originalSource = await sources.FindDurableAsync(old.GetProperty("sourceOutboxItemId").GetString()!);
        var queuedBefore = (await queue.ListAsync(new(before.ExecutionId))).Items;
        if (before.Revision != 3 || before.Lifecycle != RuntimeModelLifecycle.Running || before.TerminalResult is not null ||
            !oldOrigin.MatchesModel(before) || !current.MatchesModel(before) || oldOrigin.InvocationId != current.InvocationId ||
            oldOrigin.AttemptId == current.AttemptId || oldOrigin.WorkerExecutionId == current.WorkerExecutionId ||
            oldWorker?.Status != WorkflowExecutionStatus.Faulted || currentWorker?.Status != WorkflowExecutionStatus.Running ||
            originalReceipt?.AcceptedRevision != 2 || originalSource?.Status != RuntimePostCommitOutboxStatus.Delivered ||
            !JsonElement.DeepEquals(originalSource.Intent.Payload!.Value, old) || queuedBefore.Count != 0)
            throw new InvalidOperationException("The probe requires the actual failed attempt and one different live replacement with no queued model input.");
        var actor = await sdk.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(before.ExecutionId,
            WorkflowExecutionActorActivationReason.Recovery, started, "owned-obsolete-result-probe",
            WorkflowExecutionActorCapabilities.InProcessMailbox, partition: new(before.Scope.Value)));
        var controls = new List<object>();
        async Task Probe(string name, string inputId, JsonElement payload, bool shouldAccept)
        {
            var command = new RuntimeModelExecutionCommand(before.Executable, before.MaterialIncarnationId, inputId,
                "native-result", payload, before.Authority, before.TenantId);
            var id = "owned-obsolete-result:" + name + ":" + Guid.NewGuid().ToString("N");
            WorkflowExecutionCommandDispatchResult? outcome = null;
            string? refusalType = null; string? refusalMessage = null;
            try
            {
                outcome = await actor.EnqueueAsync(new(id, before.ExecutionId,
                    new(inputId, before.ExecutionId, WorkflowExecutionCommandKind.ExecuteModel, started,
                        JsonSerializer.SerializeToElement(command, json), new Dictionary<string, string>()),
                    id, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, started, partition: new(before.Scope.Value)));
            }
            catch (BpmnModelValidationException exception) when (!shouldAccept)
            { refusalType = exception.GetType().Name; refusalMessage = exception.Message; }
            if (shouldAccept ? outcome?.Status != WorkflowExecutionCommandDispatchStatus.Accepted : refusalType is null)
                throw new InvalidOperationException("The actual obsolete-result transport did not produce its expected acceptance/refusal.");
            var after = await reader.FindAsync(before.ExecutionId);
            var receipt = await reader.FindInputAsync(before.ExecutionId, oldOrigin.ResultInputId);
            var source = await sources.FindDurableAsync(originalSource.OutboxItemId);
            var queued = (await queue.ListAsync(new(before.ExecutionId))).Items;
            var addressed = await reader.FindInputAsync(before.ExecutionId, inputId);
            bool Same<T>(T left, T right) => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left, json), JsonSerializer.SerializeToElement(right, json));
            if (!Same(before, after) || !Same(originalReceipt, receipt) || !Same(originalSource, source) || queued.Count != 0 ||
                (!shouldAccept && inputId != oldOrigin.ResultInputId && addressed is not null))
                throw new InvalidOperationException("An old result changed the replacement, original receipt/source or accepted-input inventory.");
            controls.Add(new { name, command, outcome, refusalType, refusalMessage, model = after,
                originalReceipt = receipt, originalSource = source, addressedReceipt = addressed, queuedModelInputs = queued });
        }
        await Probe("exact-old-result-replay", oldOrigin.ResultInputId, old, true);
        var conflicting = JsonNode.Parse(old.GetRawText())!.AsObject(); conflicting["faultCode"] = "owned-probe-conflicting-code";
        await Probe("conflicting-old-result", oldOrigin.ResultInputId, JsonSerializer.SerializeToElement(conflicting, json), false);
        await Probe("newly-addressed-old-result", oldOrigin.ResultInputId + ":obsolete-probe", old, false);
        object Product(Assembly assembly) => new { name = assembly.GetName().Name,
            informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))) };
        Console.WriteLine(JsonSerializer.Serialize(new { operation = "probe-obsolete-native-result", startedAtUtc = started,
            completedAtUtc = DateTimeOffset.UtcNow, executionStateSeeded = false, oldResult = old, currentOrigin = current,
            modelBefore = before, originalReceipt, originalSource, oldWorker, currentWorker, queuedBefore, controls,
            products = new[] { Product(Assembly.GetExecutingAssembly()), Product(typeof(IWorkflowStartDispatcher).Assembly),
                Product(typeof(Elsa.Workflows.Runtime.Services.Executions.WorkflowStartDispatcher).Assembly),
                Product(typeof(RuntimeEntityFrameworkCoreOptions).Assembly), Product(typeof(ActivitiesBpmnHostingFeature).Assembly) } }, json));
    }
}
