using System.Text.Json;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Increment A0 gate (spec 123): proves the two test-harness prerequisites the fusion guardrail depends on are real,
/// BEFORE any fusion code exists — otherwise Increment C's guardrail would pass vacuously (research §8.4).
/// (P1) the harness can drive the SAME workflow coalesced (commits fold into a segment), and (P2) a
/// <see cref="ReplaySafeProbeActivity"/> node actually resolves a <see cref="SideEffectProfile.ReplaySafe"/> contract
/// while the unmarked <see cref="ProbeActivity"/> stays External.
/// </summary>
public sealed class ReplaySafeFusionHarnessPrerequisiteTests
{
    private const int HotLoopLength = 4;
    private static readonly string[] ActivityExecutionIds =
        Enumerable.Range(0, 16).Select(index => $"actexec-{index}").ToArray();

    [Fact]
    public async Task CoalescedHarnessPath_FoldsCheckpointsIntoASegment_AndStillCompletes()
    {
        var immediate = await DriveAsync(coalescing: false);
        var coalesced = await DriveAsync(coalescing: true);

        // Both runs reach the completed terminal (a silently-parked run would make any fold claim vacuous).
        Assert.True(immediate.Completed);
        Assert.True(coalesced.Completed);

        // The coalesced path genuinely folds the per-hop checkpoint storm into strictly fewer durable commits — this is
        // the burst the fusion pass engages inside. Without it, fusion never fires and the guardrail is meaningless.
        Assert.True(coalesced.PhysicalFoldCount > 0,
            "Expected the coalesced run to complete at least one provider-atomic prepared fold.");
    }

    [Fact]
    public void ReplaySafeProbeNode_ResolvesReplaySafeContract_WhileProbeStaysExternal()
    {
        var replaySafe = WorkflowExecutionHarness.NewReplaySafeProbeNode("node-1");
        var external = WorkflowExecutionHarness.NewProbeNode("node-2");

        Assert.NotNull(replaySafe.ActivityContract);
        Assert.Equal(SideEffectProfile.ReplaySafe, replaySafe.ActivityContract!.SideEffectProfile);

        Assert.NotNull(external.ActivityContract);
        Assert.Equal(SideEffectProfile.External, external.ActivityContract!.SideEffectProfile);
    }

    private static async Task<(bool Completed, int CommitCount, int PhysicalFoldCount)> DriveAsync(bool coalescing)
    {
        var foldObserver = new PreparedFoldObserver();
        var builder = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services));
        if (coalescing)
        {
            builder = builder
                .WithCoalescing()
                .ConfigureServices(services => DecorateDurableCheckpointProvider(services, foldObserver));
        }

        await using var harness = builder.Build(ActivityExecutionIds);

        var run = await harness.RunAsync(BuildReplaySafeStraightLineFlowchart());
        var commitCount = harness.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits().Count;
        return (run.WorkflowState?.Status == WorkflowExecutionStatus.Completed, commitCount, foldObserver.Count);
    }

    private static void DecorateDurableCheckpointProvider(IServiceCollection services, PreparedFoldObserver observer) =>
        CoalescingDurableCheckpointStoreTestDecorator.Decorate(
            services,
            inner => new PreparedFoldObservingCheckpointStore(
                (IRuntimeCheckpointPreparedLedgerStore)inner,
                afterFold: (_, result) =>
                {
                    if (result.Status == RuntimeCheckpointCommitStoreStatus.Committed)
                        observer.Record();
                }));

    private sealed class PreparedFoldObserver
    {
        public int Count { get; private set; }
        public void Record() => Count++;
    }

    private static WorkflowExecutable BuildReplaySafeStraightLineFlowchart()
    {
        var leaves = Enumerable.Range(0, HotLoopLength)
            .Select(index => WorkflowExecutionHarness.NewReplaySafeProbeNode($"node-loop-{index}"))
            .ToArray();
        var connections = Enumerable.Range(0, HotLoopLength - 1)
            .Select(index => new FlowchartConnection(
                new FlowchartEndpoint(leaves[index].ExecutableNodeId),
                new FlowchartEndpoint(leaves[index + 1].ExecutableNodeId)))
            .ToArray();

        var root = new ExecutableNode(
            executableNodeId: "node-flowchart",
            authoredActivityId: "authored-flowchart",
            activityType: typeof(FlowchartActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "elsa.flowchart",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, leaves)],
            structure: new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(
                    new FlowchartStructure(connections: connections, startNodeId: leaves[0].ExecutableNodeId))));

        return WorkflowExecutionHarness.NewExecutable(root);
    }
}

internal static class CoalescingDurableCheckpointStoreTestDecorator
{
    public static void Decorate(
        IServiceCollection services,
        Func<IRuntimeCheckpointCommitStore, IRuntimeCheckpointCommitStore> decorate)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(decorate);

        var serviceType = typeof(CoalescingInner<IRuntimeCheckpointCommitStore>);
        var descriptor = services.Last(item => item.ServiceType == serviceType);
        services.Remove(descriptor);
        services.Add(new ServiceDescriptor(
            serviceType,
            serviceProvider =>
            {
                var captured = (CoalescingInner<IRuntimeCheckpointCommitStore>)CreateFromDescriptor(
                    descriptor,
                    serviceProvider);
                return new CoalescingInner<IRuntimeCheckpointCommitStore>(decorate(captured.Value));
            },
            descriptor.Lifetime));
    }

    private static object CreateFromDescriptor(ServiceDescriptor descriptor, IServiceProvider serviceProvider) =>
        descriptor.ImplementationInstance ??
        descriptor.ImplementationFactory?.Invoke(serviceProvider) ??
        ActivatorUtilities.CreateInstance(serviceProvider, descriptor.ImplementationType!);
}

internal sealed class PreparedFoldObservingCheckpointStore(
    IRuntimeCheckpointPreparedLedgerStore inner,
    Action<RuntimeCheckpointPreparedFoldRequest>? beforeFold = null,
    Action<RuntimeCheckpointPreparedFoldRequest, RuntimeCheckpointPreparedFoldResult>? afterFold = null) :
    IRuntimeCheckpointPreparedLedgerStore
{
    public ValueTask<RuntimeCheckpointPreparationResult> PrepareAsync(
        RuntimeCheckpointPrepareRequest request,
        CancellationToken cancellationToken = default) =>
        inner.PrepareAsync(request, cancellationToken);

    public ValueTask<RuntimeCheckpointCommitStoreResult> CommitPreparedAsync(
        RuntimeCheckpointPreparationToken token,
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken = default) =>
        inner.CommitPreparedAsync(token, commit, decision, cancellationToken);

    public ValueTask<RuntimeCheckpointPreparedPage> PagePreparedAsync(
        RuntimeCheckpointPreparedQuery query,
        CancellationToken cancellationToken = default) =>
        inner.PagePreparedAsync(query, cancellationToken);

    public ValueTask<RuntimeCheckpointPreparedAdoptionReceipt> AdoptPreparedAsync(
        RuntimeCheckpointPreparedAdoptionRequest request,
        CancellationToken cancellationToken = default) =>
        inner.AdoptPreparedAsync(request, cancellationToken);

    public async ValueTask<RuntimeCheckpointPreparedFoldResult> CommitPreparedFoldAsync(
        RuntimeCheckpointPreparedFoldRequest request,
        CancellationToken cancellationToken = default)
    {
        beforeFold?.Invoke(request);
        var result = await inner.CommitPreparedFoldAsync(request, cancellationToken);
        afterFold?.Invoke(request, result);
        return result;
    }

    public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken = default) =>
        inner.CommitAsync(commit, decision, cancellationToken);
}
