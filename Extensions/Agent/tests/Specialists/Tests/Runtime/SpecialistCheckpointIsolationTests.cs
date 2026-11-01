using System.Reflection;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Runtime;

public sealed partial class SpecialistNativeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Unrelated_checkpoints_do_not_read_workflow_or_activity_stores_and_reuse_the_pinned_burst_artifact(bool cancel, bool burst)
    {
        await using var provider = await ServicesAsync(new FixtureFactory("{\"Article\":\"unused\"}"));
        var specialist = await ExecutableAsync(provider);
        await StartAsync(provider, specialist);
        var workflow = (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!;
        var parent = (await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run")).Single(state => state.ParentActivityExecutionId is null);
        var root = specialist.RootActivity;
        var ordinaryRoot = new ExecutableNode(root.ExecutableNodeId, root.AuthoredActivityId, "ordinary", "1",
            root.Descriptor, root.InputBindings, root.Metadata, root.ChildSlots, root.Structure, activityContract: null);
        var ordinary = new WorkflowExecutable(specialist.Identity with { ArtifactId = "ordinary", ArtifactHash = "ordinary-hash" }, ordinaryRoot,
            specialist.ResumeTargets, DateTimeOffset.UtcNow, new Dictionary<string, string>(), specialist.IncidentStrategy);
        var store = DispatchProxy.Create<IWorkflowExecutableStore, CountingCheckpointExecutableStore>();
        var counting = (CountingCheckpointExecutableStore)(object)store;
        counting.Executable = ordinary;
        var accessor = provider.GetRequiredService<IWorkflowBurstScopeAccessor>();
        await using var scope = new WorkflowBurstScope("run");
        using var ambient = accessor.Push(burst ? scope : null);
        var reader = new BurstCachedWorkflowExecutableReader(store, accessor);
        if (burst) _ = await reader.FindAsync(ordinary.Identity.ArtifactId); // The drain already loaded its immutable executable.
        counting.Reads = 0;
        counting.ForbidReads = burst;
        var workflows = DispatchProxy.Create<IWorkflowExecutionStateStore, ForbiddenCheckpointStore>();
        var activities = DispatchProxy.Create<IActivityExecutionStateStore, ForbiddenCheckpointStore>();
        var boundary = new SpecialistSettlementBoundary(workflows, activities, store, provider.GetRequiredService<IPersistenceAccessContextAccessor>());
        var enricher = new SpecialistCheckpointEnricher(boundary, activities, reader);
        var now = DateTimeOffset.UtcNow;
        parent = parent with { Status = cancel ? ActivityExecutionStatus.Cancelled : ActivityExecutionStatus.Completed, CompletedAt = now };
        workflow = workflow with { PinnedExecutable = ordinary.Identity, Status = WorkflowExecutionStatus.Cancelled, CompletedAt = now };
        var changes = new RuntimeCheckpointStateChangeSet(cancel ? new("run", RuntimeStateChangeOperation.Upsert, workflow, new Dictionary<string, string>()) : null,
            null, [new(parent.InvocationId, RuntimeStateChangeOperation.Upsert, parent, new Dictionary<string, string>())], [], [], [], []);
        var pin = new Dictionary<string, string> { [RuntimeMetadataKeys.ExecutableArtifactId] = ordinary.Identity.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = ordinary.Identity.ArtifactHash };
        var commit = new RuntimeCheckpointCommit("ordinary-commit", new("ordinary-checkpoint",
            cancel ? RuntimeCheckpointNames.ActivityCancelled : RuntimeCheckpointNames.ActivityCompleted, "run", now, [parent.InvocationId], pin), changes, [], new Dictionary<string, string>());
        var enriched = await enricher.EnrichAsync(commit);
        await enricher.ValidateFinalAsync(enriched);
        Assert.Empty(enriched.PostCommitIntents);
        Assert.Equal(burst ? 0 : 1, counting.Reads);
        Assert.Equal(0, ((ForbiddenCheckpointStore)(object)workflows).Calls);
        Assert.Equal(0, ((ForbiddenCheckpointStore)(object)activities).Calls);
        var injected = new RuntimePostCommitIntent("forged", "run", SpecialistSettlementBoundary.CancelKind, now, parent.InvocationId, null, null);
        _ = await enricher.EnrichAsync(enriched);
        await Assert.ThrowsAsync<SpecialistTurnException>(() => enricher.ValidateFinalAsync(enriched with { PostCommitIntents = [injected] }).AsTask());
        var altered = new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []);
        // Ordinary state can be enriched later; fabricated specialist responsibility cannot.
        _ = await enricher.EnrichAsync(enriched);
        await enricher.ValidateFinalAsync(enriched with { StateChanges = altered });
    }

    [Fact]
    public async Task Workflow_only_specialist_cancellation_refuses_a_later_nonterminal_operation_key_change()
    {
        await using var provider = await ServicesAsync(new FixtureFactory("{\"Article\":\"unused\"}"));
        var executable = await ExecutableAsync(provider);
        await StartAsync(provider, executable);
        var workflow = (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!;
        var parent = (await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run")).Single(state => state.ParentActivityExecutionId is null);
        var now = DateTimeOffset.UtcNow;
        var terminal = workflow with { Status = WorkflowExecutionStatus.Cancelled, CompletedAt = now };
        var changes = new RuntimeCheckpointStateChangeSet(new("run", RuntimeStateChangeOperation.Upsert, terminal, new Dictionary<string, string>()),
            null, [], [], [], [], []);
        var commit = new RuntimeCheckpointCommit("workflow-only-cancel", new("workflow-only-cancel-checkpoint", RuntimeCheckpointNames.ActivityCancelled,
            "run", now, [], new Dictionary<string, string>()), changes, [], new Dictionary<string, string>());
        var enricher = provider.GetRequiredService<IEnumerable<IRuntimeCheckpointCommitEnricher>>().OfType<SpecialistCheckpointEnricher>().Single();
        var enriched = await enricher.EnrichAsync(commit);
        Assert.Single(enriched.PostCommitIntents);
        var changed = parent with { Provenance = parent.Provenance with { Metadata = new Dictionary<string, string>(parent.Provenance.Metadata)
            { [RuntimeMetadataKeys.OperationKey] = "changed-after-cancellation-enrichment" } } };
        Assert.NotEqual(parent.OperationKey, changed.OperationKey);
        await Assert.ThrowsAsync<SpecialistTurnException>(() => enricher.ValidateFinalAsync(enriched with
        {
            StateChanges = changes.WithActivityExecutions([new(parent.InvocationId, RuntimeStateChangeOperation.Upsert, changed, new Dictionary<string, string>())])
        }).AsTask());
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Actual_later_control_producer_can_replace_a_terminal_checkpoint_without_changing_specialist_responsibility(bool cancel, bool facade, bool alreadyTerminal)
    {
        await using var provider = await ServicesAsync(new FixtureFactory("{\"Article\":\"unused\"}"));
        var specialist = await ExecutableAsync(provider);
        await StartAsync(provider, specialist);
        var original = (await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run")).Single(state => state.ParentActivityExecutionId is null);
        var address = new RuntimeChildControlTarget("control-parent", "control-call", "control-consumer", specialist.Identity, original.InvocationId, "test.control", 1);
        var now = DateTimeOffset.UtcNow;
        var root = original with { Execution = original.Execution with { WorkflowExecutionId = address.ChildWorkflowExecutionId },
            Status = cancel && facade && !alreadyTerminal ? original.Status : cancel ? ActivityExecutionStatus.Cancelled : ActivityExecutionStatus.Completed,
            CompletedAt = cancel && facade && !alreadyTerminal ? null : now };
        var activities = provider.GetRequiredService<IActivityExecutionStateStore>();
        await activities.SaveAsync(root);
        var workflow = (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))! with
        { WorkflowExecutionId = address.ChildWorkflowExecutionId, Status = cancel ? WorkflowExecutionStatus.Cancelled : WorkflowExecutionStatus.Completed, CompletedAt = now };
        var dispatch = new WorkflowDispatchRecord(address.DispatchId, address.ParentWorkflowExecutionId, address.CallingActivityExecutionId,
            address.ChildWorkflowExecutionId, specialist.Identity, new("source", "test", "source", "1", "workflow", "workflow-v1", "1.0.0", null, null),
            WorkflowDispatchMode.WaitForCompletion, cancel ? WorkflowDispatchStatus.Cancelled : WorkflowDispatchStatus.Completed,
            null, workflow.TenantId, workflow.Partition!, WorkflowRunKind.Unknown, WorkflowExecutionAuthoritySnapshot.CreateRoot("test"), [], now, now,
            new Dictionary<string, string> { [RuntimeMetadataKeys.ChildControlConsumerKey] = "test.control", [RuntimeMetadataKeys.ChildControlConsumerVersion] = "1" });
        var changes = new RuntimeCheckpointStateChangeSet(new(workflow.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, workflow, new Dictionary<string, string>()),
            null, [], [], [], [], [], workflowDispatches: [new(dispatch.DispatchId, RuntimeStateChangeOperation.Upsert, dispatch, new Dictionary<string, string>())]);
        var commit = new RuntimeCheckpointCommit("control-composition", new("control-composition-checkpoint", RuntimeCheckpointNames.WorkflowCompleted,
            workflow.WorkflowExecutionId, now, [], new Dictionary<string, string>()), changes, [], new Dictionary<string, string>());
        // Exercise both ordinary workflows and a specialist root's cancellation responsibility.
        var node = specialist.RootActivity;
        var ordinary = new WorkflowExecutable(specialist.Identity,
            new ExecutableNode(node.ExecutableNodeId, node.AuthoredActivityId, "ordinary", "1", node.Descriptor, node.InputBindings,
                node.Metadata, node.ChildSlots, node.Structure, activityContract: null), specialist.ResumeTargets, now, new Dictionary<string, string>(), specialist.IncidentStrategy);
        var store = DispatchProxy.Create<IWorkflowExecutableStore, CountingCheckpointExecutableStore>();
        ((CountingCheckpointExecutableStore)(object)store).Executable = facade ? specialist : ordinary;
        var nativeWorkflows = DispatchProxy.Create<IWorkflowExecutionStateStore, ForbiddenCheckpointStore>();
        var nativeActivities = DispatchProxy.Create<IActivityExecutionStateStore, ForbiddenCheckpointStore>();
        await provider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(workflow with { Status = WorkflowExecutionStatus.Running, CompletedAt = null });
        var selectedActivities = facade ? activities : nativeActivities;
        var selectedWorkflows = facade ? provider.GetRequiredService<IWorkflowExecutionStateStore>() : nativeWorkflows;
        var specialistEnricher = new SpecialistCheckpointEnricher(new(selectedWorkflows, selectedActivities, store,
            provider.GetRequiredService<IPersistenceAccessContextAccessor>()), selectedActivities,
            new BurstCachedWorkflowExecutableReader(store, provider.GetRequiredService<IWorkflowBurstScopeAccessor>()));
        var registrations = new ServiceCollection();
        new Elsa.Activities.DispatchWorkflow.Runtime.DispatchWorkflowRuntimeFeature().ConfigureServices(registrations);
        var producerType = registrations.Single(d => d.ServiceType == typeof(IRuntimeCheckpointCommitEnricher) &&
            d.ImplementationType?.Name == "WorkflowControlProducerEnricher").ImplementationType!;
        var producer = (IRuntimeCheckpointCommitEnricher)ActivatorUtilities.CreateInstance(provider, producerType, activities, new CheckpointControlBoundary(address));
        var enriched = await specialistEnricher.EnrichAsync(commit);
        var final = await producer.EnrichAsync(enriched);
        Assert.Equal(cancel && facade && !alreadyTerminal ? 1 : 0, final.PostCommitIntents.Count);
        Assert.NotSame(enriched.Checkpoint, final.Checkpoint);
        Assert.True(Assert.Single(final.StateChanges.ActivityExecutions).State.ProducedControl!.IsTerminal);
        await specialistEnricher.ValidateFinalAsync(final);
        Assert.Equal(0, ((ForbiddenCheckpointStore)(object)nativeWorkflows).Calls);
        Assert.Equal(0, ((ForbiddenCheckpointStore)(object)nativeActivities).Calls);
    }

    private sealed class CheckpointControlBoundary(RuntimeChildControlTarget address) : IRuntimeWorkflowControlBoundary
    {
        public ValueTask<RuntimeChildControlTarget?> ResolveAsync(string workflowExecutionId, string rootActivityExecutionId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(address.ChildWorkflowExecutionId, workflowExecutionId);
            Assert.Equal(address.RootActivityExecutionId, rootActivityExecutionId);
            return ValueTask.FromResult<RuntimeChildControlTarget?>(address);
        }
    }

    public class ForbiddenCheckpointStore : DispatchProxy
    {
        public int Calls { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls++;
            throw new InvalidOperationException("An unrelated checkpoint must not consult the native state stores.");
        }
    }

    public class CountingCheckpointExecutableStore : DispatchProxy
    {
        public WorkflowExecutable Executable { get; set; } = null!;
        public int Reads { get; set; }
        public bool ForbidReads { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.Equal(nameof(IWorkflowExecutableStore.FindAsync), targetMethod!.Name);
            Reads++;
            if (ForbidReads) throw new InvalidOperationException("The drain already loaded its pinned executable.");
            Assert.Equal(Executable.Identity.ArtifactId, args![0]);
            return ValueTask.FromResult<WorkflowExecutable?>(Executable);
        }
    }
}
