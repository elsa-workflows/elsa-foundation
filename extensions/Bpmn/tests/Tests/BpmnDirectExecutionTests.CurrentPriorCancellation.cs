using System.Text.Json;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using RunBpmnState = Elsa.Activities.Bpmn.Models.RunBpmnState;
using RuntimeDelay = Elsa.Activities.Scheduling.Activities.Delay;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Runtime.Api.Coalescing;
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
    public async Task A_current_writer_preserves_prior_scope_cancellation_across_queued_result_and_cold_restart()
    {
        string outer, middle, inner, source;
        PriorCancellationCustody original;
        await using (var host = await OpenAsync(configure: ConfigurePriorCancellationServices,
                         activityIds: Enumerable.Range(1, 64).Select(i => "prior-cancellation-" + i).ToArray()))
        {
            await using var scope = await ScopeAsync(host);
            var services = scope.ServiceProvider;
            var runtime = services.GetRequiredService<IBpmnModelRuntime>();
            var caller = new BpmnModelCaller("operator", null);
            // The earlier cancellation must own real native work. A human-only inner
            // model can settle immediately and does not exercise this debt boundary.
            var delayContract = ClrActivityContractTestBuilder.BuildContract(typeof(RuntimeDelay));
            var duration = delayContract.Inputs["Duration"];
            var delayNode = new ExecutableNode("prior-delay", "authored-prior-delay", typeof(RuntimeDelay).FullName!, "1.0.0",
                delayContract.DescriptorKind, delayContract.DescriptorPayload,
                ClrActivityContractTestBuilder.CompleteInputBindings(delayContract, new Dictionary<string, RuntimeInputBinding>
                {
                    [duration.Key] = new(duration.Key, duration.Type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                        literal: ValueEnvelope.Inline(duration.Type, JsonSerializer.SerializeToElement(TimeSpan.FromHours(1)), ValueProtectionPolicy.InstanceInline))
                }), new Dictionary<string, string>(), activityContract: delayContract);
            var delayResume = typeof(RuntimeDelay).GetMethod("ResumeAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetCustomAttributes(typeof(ResumeTargetAttribute), false).Cast<ResumeTargetAttribute>().Single().ResumeTargetId;
            var delayTarget = WorkflowExecutableResumeTarget.ComposeScopedId(delayNode.ExecutableNodeId, delayResume);
            var hasher = services.GetRequiredService<IWorkflowExecutableHasher>();
            var inputContract = new WorkflowExecutableInputContract(1, []);
            var delayHash = hasher.ComputeHash(delayNode, inputContract, [], incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
            var delayIdentity = new WorkflowExecutableIdentity(hasher.CreateArtifactId("s9-prior-delay", delayHash), "s9-prior-delay", "s9-prior-delay-v1", "1.0.0", delayHash);
            await host.PublishAsync(new(delayIdentity, delayNode,
                new Dictionary<string, WorkflowExecutableResumeTarget> { [delayTarget] = new(delayTarget, delayNode.ExecutableNodeId, "ResumeAsync", new Dictionary<string, string>(), delayResume) },
                DateTimeOffset.UtcNow, new Dictionary<string, string>(), inputContract, null, IncidentStrategyBuiltIns.FaultReference), "s9-prior-delay");
            var innerProcess = new BpmnProcessDefinition("s9-prior-inner", Elements:
                [new("start", BpmnElementTypes.StartEvent), new("service", BpmnElementTypes.ServiceTask, bindingRef: "delay"), new("end", BpmnElementTypes.EndEvent)],
                SequenceFlows: [new("begin", "start", "service"), new("finish", "service", "end")]);
            var nested = await runtime.PublishAsync(new(2, JsonSerializer.SerializeToElement(innerProcess, Web), [], [new("delay", delayIdentity, "delay.error")]), "s9-prior-inner", caller);
            var nestedMaterial = (await services.GetRequiredService<IWorkflowExecutableStore>().FindMaterialAsync(nested.Executable.ArtifactId))!;
            var pin = new BpmnEmbeddingPin(nested.SourceReferenceId, nestedMaterial.Identity, nestedMaterial.IncarnationId);
            var contract = ClrActivityContractTestBuilder.BuildContract(typeof(RunBpmn));
            var input = contract.Inputs[nameof(RunBpmn.Model)];
            var binding = new RuntimeInputBinding(input.Key, input.Type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(input.Type, JsonSerializer.SerializeToElement(nested.SourceReferenceId), ValueProtectionPolicy.InstanceInline));
            const string nodeId = "prior-run-bpmn";
            var node = new ExecutableNode(nodeId, "authored-" + nodeId, RunBpmn.ActivityType, "1.0.0", contract.DescriptorKind, contract.DescriptorPayload,
                ClrActivityContractTestBuilder.CompleteInputBindings(contract, new Dictionary<string, RuntimeInputBinding> { [input.Key] = binding }),
                new Dictionary<string, string> { [BpmnEmbeddingBoundary.PinKey] = BpmnEmbeddingJson.Serialize(pin), ["activity.definitionVersionId"] = "s9-prior-reusable-v1" },
                activityContract: contract);
            var targetId = WorkflowExecutableResumeTarget.ComposeScopedId(nodeId, BpmnEmbeddingBoundary.ResumeKey);
            var targets = new Dictionary<string, WorkflowExecutableResumeTarget> { [targetId] = new(targetId, nodeId, "ResumeAsync", new Dictionary<string, string>(), BpmnEmbeddingBoundary.ResumeKey) };
            WorkflowExecutableDependency[] dependencies = [new(nested.Executable.ArtifactId, nested.Executable.ArtifactHash, [nodeId])];
            var hash = hasher.ComputeHash(node, inputContract, dependencies, incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
            var identity = new WorkflowExecutableIdentity(hasher.CreateArtifactId("s9-prior", hash), "s9-prior-worker", "s9-prior-worker-v1", "1.0.0", hash);
            var worker = new WorkflowExecutable(identity, node, targets, DateTimeOffset.UtcNow, new Dictionary<string, string>(), inputContract, dependencies, IncidentStrategyBuiltIns.FaultReference);
            Assert.True(hasher.MatchesHash(worker));
            await host.PublishAsync(worker, "s9-prior-worker-source");
            var process = new BpmnProcessDefinition("s9-prior-outer", Elements:
                [new("start", BpmnElementTypes.StartEvent), new("service", BpmnElementTypes.ServiceTask, bindingRef: "worker"), new("end", BpmnElementTypes.EndEvent)],
                SequenceFlows: [new("begin", "start", "service"), new("finish", "service", "end")]);
            var publication = await runtime.PublishAsync(new(2, JsonSerializer.SerializeToElement(process, Web), [], [new("worker", identity, "worker.error")]), "s9-prior-outer", caller);
            outer = (await runtime.StartAsync(publication.SourceReferenceId, "s9-prior-start", null, caller)).ExecutionId;
            var reader = services.GetRequiredService<IRuntimeModelExecutionReader>();
            var processor = services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
            middle = Assert.Single(BpmnDirectExecutionDriver.Read((await reader.FindAsync(outer))!).Work).NativeWork!.WorkerExecutionId;
            await processor.ProcessAsync(new(1, outer, BpmnNativeWorkBoundary.StartKind));
            var child = Assert.Single(await services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(middle), activity => activity.Execution.ActivityType == RunBpmn.ActivityType);
            var invocation = BpmnEmbeddingBoundary.Read(child.PrivateState!.Value.InlineValue!.Value.Deserialize<RunBpmnState>(Web)!);
            inner = invocation.ExecutionId;
            await processor.ProcessAsync(new(1, middle, BpmnEmbeddingBoundary.StartKind));
            await SendPriorCancellationAsync(host, inner, WorkflowExecutionCommandKind.RunSchedulerWork);
            await processor.ProcessAsync(new(1, inner, BpmnNativeWorkBoundary.StartKind));
            await SendPriorCancellationAsync(host, middle, WorkflowExecutionCommandKind.CancelActivityScope,
                JsonSerializer.SerializeToElement(new CancelActivityScopeCommand(child.InvocationId, child.ExecutionScopeId!, "Cancel the accepted embedded scope"), Web));
            var outbox = services.GetRequiredService<IRuntimePostCommitOutboxStore>();
            source = Assert.Single(await outbox.GetDeliverableAsync(new(DateTimeOffset.UtcNow, 20, middle, BpmnEmbeddingBoundary.CancelKind))).OutboxItemId;
            await processor.ProcessAsync(new(1, middle, BpmnEmbeddingBoundary.CancelKind));
            await SendPriorCancellationAsync(host, inner, WorkflowExecutionCommandKind.RunSchedulerWork);
            await Task.Delay(1200);
            await processor.ProcessAsync(new(1, middle, BpmnEmbeddingBoundary.CancelKind));
            await SendPriorCancellationAsync(host, middle, WorkflowExecutionCommandKind.Cancel);
            await processor.ProcessAsync(new(1, middle, BpmnNativeWorkBoundary.ResultKind));
            Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable,
                (await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(source))!.Status);
            original = await ReadPriorCancellationCustodyAsync(services, outer, inner, source);
            await CapturePriorCancellationAsync("pending-outer-result", services, outer, middle, inner, source);
        }
        await using var reopened = await OpenAsync(admitNewExecutions: false, configure: ConfigurePriorCancellationServices);
        await RecoverPriorCancellationAsync(reopened, outer, middle, inner, source, original);
    }

    private static void ConfigurePriorCancellationServices(IServiceCollection services)
    {
        new Elsa.Activities.Scheduling.ActivitiesSchedulingFeature().ConfigureServices(services);
        new Elsa.Workflows.Runtime.Scheduling.WorkflowsRuntimeSchedulingFeature().ConfigureServices(services);
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(TimeProvider.System));
    }

    private async Task RecoverPriorCancellationAsync(WorkflowExecutionHarness host, string outer, string middle, string inner, string source, PriorCancellationCustody original)
    {
        await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        var reader = services.GetRequiredService<IRuntimeModelExecutionReader>();
        var lookup = services.GetRequiredService<IPostCommitOutboxLookupStore>();
        var queue = services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var reopened = await ReadPriorCancellationCustodyAsync(services, outer, inner, source);
        Assert.Equal(JsonSerializer.Serialize(original.Outer, Web), JsonSerializer.Serialize(reopened.Outer, Web));
        Assert.Equal(original.Inner, reopened.Inner);
        Assert.Equal(original.Native, reopened.Native);
        Assert.Equal(original.Queue, reopened.Queue);
        Assert.Equal(original.Cancellation, reopened.Cancellation);
        var before = original.Outer;
        var command = Assert.Single((await queue.ListAsync(new(outer))).Items).CommandId;
        Assert.Equal(RuntimeModelLifecycle.Running, before.Lifecycle);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, (await lookup.FindDurableAsync(source))!.Status);
        await SendPriorCancellationAsync(host, outer, WorkflowExecutionCommandKind.RunSchedulerWork);
        Assert.Equal(before.Revision, (await reader.FindAsync(outer))!.Revision);
        Assert.Null(await reader.FindInputAsync(outer, command));
        Assert.Equal(command, Assert.Single((await queue.ListAsync(new(outer))).Items).CommandId);
        Assert.Null((await lookup.FindDurableAsync(source))!.DeliveredAt);
        var innerBefore = (await reader.FindAsync(inner))!;
        Assert.Equal(RuntimeModelLifecycle.TerminalPendingSettlement, innerBefore.Lifecycle);
        var descendant = Assert.Single(BpmnDirectExecutionDriver.Read(innerBefore).Cancellations!).Origin.WorkerExecutionId;
        await CapturePriorCancellationAsync("deferred-original-result", services, outer, middle, inner, source);
        var processor = services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        while (DateTimeOffset.UtcNow < deadline && (await reader.FindAsync(outer))!.Lifecycle != RuntimeModelLifecycle.TerminalSettled)
        {
            foreach (var execution in new[] { descendant, inner, middle, outer })
            {
                await processor.ProcessAsync(new(100, execution));
                await SendPriorCancellationAsync(host, execution, WorkflowExecutionCommandKind.RunSchedulerWork);
            }
            await Task.Delay(1300);
        }
        var final = (await reader.FindAsync(outer))!;
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, final.Lifecycle);
        Assert.Equal("Cancelled", final.TerminalResult!.Value.GetProperty("status").GetString());
        Assert.Equal(before.Executable, final.Executable);
        Assert.Equal(before.MaterialIncarnationId, final.MaterialIncarnationId);
        Assert.True(WorkflowExecutionAuthoritySnapshot.Matches(final.Authority, before.Authority.SystemIdentity, before.Authority.RootInitiator, before.Authority.Metadata));
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, (await reader.FindAsync(inner))!.Lifecycle);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await lookup.FindDurableAsync(source))!.Status);
        Assert.Equal("Accepted", (await reader.FindInputAsync(outer, command))!.Result.GetProperty("status").GetString());
        await CapturePriorCancellationAsync("settled-after-restart", services, outer, middle, inner, source);
    }

    private sealed record PriorCancellationCustody(RuntimeModelExecutionState Outer, string Inner, string Native, string Queue, string Cancellation);

    private static async Task<PriorCancellationCustody> ReadPriorCancellationCustodyAsync(IServiceProvider services, string outer, string inner, string source)
    {
        var reader = services.GetRequiredService<IRuntimeModelExecutionReader>();
        return new((await reader.FindAsync(outer))!, JsonSerializer.Serialize(await reader.FindAsync(inner), Web),
            JsonSerializer.Serialize((await services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync()).OrderBy(state => state.WorkflowExecutionId, StringComparer.Ordinal), Web),
            JsonSerializer.Serialize(Assert.Single((await services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new(outer))).Items), Web),
            JsonSerializer.Serialize(await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(source), Web));
    }

    private async Task CapturePriorCancellationAsync(string stage, IServiceProvider services, string outer, string middle, string inner, string source) =>
        await CaptureCurrentFixtureAsync("prior-scope-cancellation", stage, services, outer, new
        {
            outer, middle, inner, source,
            innerState = await services.GetRequiredService<IRuntimeModelExecutionReader>().FindAsync(inner),
            cancellation = await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(source),
            originalResult = (await services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new(outer))).Items
        });

    private static async Task SendPriorCancellationAsync(WorkflowExecutionHarness host, string execution, WorkflowExecutionCommandKind kind, JsonElement? payload = null)
    {
        var now = DateTimeOffset.UtcNow;
        var id = "prior-cancel:" + Guid.NewGuid().ToString("N");
        var actor = await host.Services.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(execution,
            WorkflowExecutionActorActivationReason.ControlPlaneCommand, now, "prior-cancel-test", WorkflowExecutionActorCapabilities.InProcessMailbox, partition: new(PersistenceScope.DefaultValue)));
        try
        {
            await actor.EnqueueAsync(new(id, execution, new(id, execution, kind, now, payload, new Dictionary<string, string>()), id,
                WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now, partition: new(PersistenceScope.DefaultValue)));
        }
        catch (RuntimeSchedulerWorkUnavailableException) when (kind == WorkflowExecutionCommandKind.RunSchedulerWork) { /* Keep the original positive claim for normal visibility expiry. */ }
    }
}
