using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_terminal_owner_precedes_history_capture_in_either_registration_order(bool captureRegisteredFirst)
    {
        var at = WorkflowExecutionHarness.Timestamp;
        var native = new WorkflowExecutable(WorkflowExecutionHarness.Identity, WorkflowExecutionHarness.NewProbeNode("history-root"),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), at, new Dictionary<string, string>(),
            inputContract: new(1, []), dependencies: null, incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
        var modelPin = new WorkflowExecutableIdentity("model-artifact", "model-definition", "model-version", "1", "model-hash");
        var origin = new RuntimeModelNativeWorkOrigin(1, new("tenant-a"), "model-run", new(BpmnModelProtocol.Key, BpmnModelProtocol.Version),
            modelPin, "model-incarnation", "binding", "invocation", "attempt", native.Identity, "worker-run", "start-commit:start-intent", "start-intent",
            BpmnNativeWorkBoundary.ResultKind, "result-input");
        var policy = new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true, ["WorkflowCompleted"]);
        var prior = new WorkflowExecutionState("worker-run", native.Identity, WorkflowExecutionStatus.Running, null,
            at, at, at, null, null, null, "tenant-a", new Dictionary<string, string>())
        { ModelWorkOrigin = origin, Partition = new("tenant-a"), HistoryCapturePolicy = policy, HistoryCaptureEnabledAtAdmission = true };
        var root = new ActivityExecutionState(new("root", "worker-run", "history-root", "root-authored", "test", "1"),
            ActivityExecutionStatus.Completed, null, 1, at, at, at, null, null, null, null,
            ActivitySchedulingProvenance.From("worker-run", null, null, null, null, null, null, "test"), null, [], [], 0, 0, new Dictionary<string, string>())
        { Completion = new("root", "root-attempt", ValueEnvelope.Null(new ValueTypeDescriptor("test/result"), ValueProtectionPolicy.InstanceInline), "Done", at, "contract") };
        var executions = DeliveryFake<IWorkflowExecutionStateStore>((method, _) => method.Name == "FindAsync"
            ? ValueTask.FromResult<WorkflowExecutionState?>(prior) : throw new InvalidOperationException(method.Name));
        var activities = DeliveryFake<IActivityExecutionStateStore>((method, args) => method.Name == "ListPageAsync"
            ? ValueTask.FromResult(new RuntimeStorePage<ActivityExecutionState>((ActivityExecutionStatePageQuery)args![0]!, [root])) : throw new InvalidOperationException(method.Name));
        var materials = DeliveryFake<IWorkflowExecutableStore>((method, _) => method.Name == "FindMaterialAsync"
            ? ValueTask.FromResult<RuntimeExecutionMaterial?>(new(native, "native-incarnation")) : throw new InvalidOperationException(method.Name));
        var outbox = DeliveryFake<DeliveryOutbox>();
        RuntimeCheckpointCommit? received = null;
        var writer = DeliveryFake<IRuntimeCheckpointCommitStore>((method, args) => method.Name switch
        {
            "get_SupportsModelAdmission" or "CanCommitModelAdmission" or "get_SupportsHistory" or "CanCommitHistory" => true,
            "CommitAsync" => ReachWrite((RuntimeCheckpointCommit)args![0]!),
            _ => throw new InvalidOperationException(method.Name)
        });
        object ReachWrite(RuntimeCheckpointCommit commit) { received = commit; throw new HistoryOrderingWriteReached(); }
        var boundary = new BpmnNativeWorkBoundary(materials, outbox, executions, activities, writer, outbox,
            new InspectionAccess(), TimeProvider.System, DeliveryFake<IWorkflowSchedulerWorkQueue>(), DeliveryFake<IRuntimeModelExecutionReader>());
        var owner = new BpmnNativeWorkCheckpointEnricher(boundary);
        var source = new DefaultRuntimeHistorySourceExtension(executions);
        RuntimeCheckpointCommitter? committer = null;
        var services = new ServiceCollection();
        services.AddSingleton<IRuntimeHistorySourceExtension>(source);
        services.AddSingleton<RuntimeCheckpointCommitter>(_ => committer!);
        using var provider = services.BuildServiceProvider();
        var capture = new RuntimeHistoryCapture(executions, [new(Enum.GetValues<RuntimeHistorySourceFactKind>())],
            new RuntimeHistoryCaptureCapabilities(provider));
        RuntimeCheckpointCommitter Pipeline(IRuntimeCheckpointCommitEnricher observer) => new(
            new ImmediateRuntimeCheckpointPersistencePolicy(), writer, new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),
            captureRegisteredFirst ? [observer, owner] : [owner, observer], [])
        { HistoryStore = DeliveryFake<IRuntimeHistoryStore>(), HistorySourceResolver = () => source };
        committer = Pipeline(new OriginalHistoryPhase(capture));
        Assert.True(capture.CanCapture(policy));
        var terminal = prior with { Status = WorkflowExecutionStatus.Completed, CompletedAt = at };
        var commit = new RuntimeCheckpointCommit("native-terminal", new("native-terminal-checkpoint", RuntimeCheckpointNames.WorkflowCompleted,
            "worker-run", at, [], new Dictionary<string, string>()),
            new(new("worker-run", RuntimeStateChangeOperation.Upsert, terminal, new Dictionary<string, string>()), null, [], [], [], [], []),
            [], new Dictionary<string, string>());
        // Execute the original relative phase as a negative control against the actual owner and committer.
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () => await committer.CommitAsync(commit));
        Assert.Contains("source changed", refused.Message, StringComparison.Ordinal);
        Assert.Null(received);
        committer = Pipeline(capture);
        await Assert.ThrowsAsync<HistoryOrderingWriteReached>(async () => await committer.CommitAsync(commit));
        Assert.NotNull(received);
        Assert.Single(received.PostCommitIntents, intent => intent.Kind == BpmnNativeWorkBoundary.ResultKind);
        Assert.Equal("WorkflowCompleted", Assert.Single(Assert.Single(received.HistoryBatches!).Candidates).Event.Name);
    }

    private sealed class OriginalHistoryPhase(IRuntimeCheckpointCommitEnricher inner) : IRuntimeCheckpointCommitEnricher
    {
        public int Order => 2000;
        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken token = default) =>
            inner.EnrichAsync(commit, token);
        public ValueTask ValidateFinalAsync(RuntimeCheckpointCommit commit, CancellationToken token = default) =>
            inner.ValidateFinalAsync(commit, token);
    }

    private sealed class HistoryOrderingWriteReached : Exception { }
}
