using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Activities.Testing;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class QueuedSelectedStartTests
{
    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    public async Task Selected_leaf_uses_real_dispatch_and_atomic_root_checkpoint_without_invoking_ancestors_or_siblings(PersistenceMode mode)
    {
        var owner = new Owner();
        await using var harness = Build(owner, mode);
        await Setup(harness, owner);
        var before = JsonSerializer.Serialize(owner.Executable);
        var result = await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun);
        Assert.True(result.CommandDispatch.Status == WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch.Reason);
        var state = await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId);
        Assert.NotNull(state);
        Assert.Equal(WorkflowExecutionStatus.Completed, state.Status);
        Assert.True(owner.Entry!.Matches(state.TestEntry));
        Assert.Equal(owner.Executable!.Identity, state.PinnedExecutable);
        var activity = Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
        Assert.Equal("selected", activity.Execution.ExecutableNodeId);
        Assert.Null(activity.ParentActivityExecutionId);
        Assert.Equal(ActivityExecutionStatus.Completed, activity.Status);
        Assert.Equal(before, JsonSerializer.Serialize(await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(owner.Executable.Identity.ArtifactId)));
        Assert.True(owner.SeedReads >= 3);
    }

    [Theory]
    [InlineData("teardown")]
    [InlineData("expiry")]
    public async Task Scope_change_after_preparation_refuses_the_atomic_first_commit_and_invokes_no_activity(string profile)
    {
        var owner = new Owner();
        await using var harness = Build(owner, PersistenceMode.Balanced, services =>
            services.AddScoped<IRuntimeCheckpointCommitEnricher>(sp => new BeforeCommit(owner, sp, profile)));
        await Setup(harness, owner);
        var result = await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun);
        Assert.True(owner.AtomicHookRan, result.CommandDispatch.Reason);
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }

    [Fact]
    public async Task Expiry_while_acquiring_the_root_lease_cannot_commit_or_schedule_the_selected_leaf()
    {
        var owner = new Owner();
        await using var harness = Build(owner, PersistenceMode.Balanced, services =>
            services.Replace(ServiceDescriptor.Scoped<IWorkflowExecutableRootWriteLeaseManager>(sp =>
                new ExpireAtRootLease(owner, ActivatorUtilities.CreateInstance<WorkflowExecutableRootWriteLeaseManager>(sp)))));
        await Setup(harness, owner);
        _ = await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun);
        Assert.True(owner.LeaseHookRan);
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }

    [Theory]
    [InlineData("missing-profile")]
    [InlineData("effect-leaf")]
    [InlineData("composite")]
    public async Task Unqualified_effects_or_owned_children_never_reach_activity_execution(string profile)
    {
        var owner = new Owner();
        await using var harness = Build(owner, PersistenceMode.Balanced, services =>
        { if (profile == "missing-profile") services.RemoveAll<IRuntimeSelectedStartTargetProfile>(); });
        var target = profile == "effect-leaf" ? new ExecutableNode("selected", "selected", typeof(EffectLeaf).FullName!, "1", "effect",
            JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()) :
            profile == "composite" ? new ExecutableNode("selected", "selected", typeof(ProbeActivity).FullName!, "1", "composite",
                JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
                childSlots: [new("owned", [WorkflowExecutionHarness.NewProbeNode("effect-child")])]) : null;
        await Setup(harness, owner, target);
        EffectLeaf.Invocations = 0;
        await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () =>
            await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun));
        Assert.Equal(0, EffectLeaf.Invocations);
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }

    public sealed class EffectLeaf : Activity<ActivityUnit>
    {
        internal static int Invocations;
        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        { Invocations++; return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value)); }
    }

    private sealed class ExpireAtRootLease(Owner owner, IWorkflowExecutableRootWriteLeaseManager inner) : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) =>
            inner.ExecuteAsync(artifactId, leaseId, write, cancellationToken);
        public ValueTask ExecuteAsync(WorkflowExecutableIdentity root, string leaseId, Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default) => inner.ExecuteAsync(root, leaseId, ct =>
            {
                if (owner.Entry is not null && !owner.LeaseHookRan && leaseId.StartsWith("checkpoint:", StringComparison.Ordinal))
                { owner.LeaseHookRan = true; owner.Clock.Now = owner.Source!.ExpiresAt < owner.Entry.Scope.ExpiresAt ? owner.Source.ExpiresAt.Value : owner.Entry.Scope.ExpiresAt; }
                return write(ct);
            }, cancellationToken);
    }

    [Theory]
    [InlineData("ParentActivityExecutionId")]
    [InlineData("SchedulingActivityExecutionId")]
    [InlineData("ExecutionScopeId")]
    [InlineData("IterationId")]
    [InlineData("metadata-scope")]
    [InlineData("work-scope")]
    public async Task Forged_first_checkpoint_ancestry_or_scope_is_refused_before_root_creation(string profile)
    {
        var owner = new Owner();
        await using var harness = Build(owner, PersistenceMode.Balanced);
        await Setup(harness, owner);
        var work = await StageCheckpoint(harness, owner);
        var json = JsonNode.Parse(work.Payload!.Value.GetRawText())!;
        var rootWork = json["PostCommitIntents"]![0]!["Payload"]!;
        var provenance = rootWork["Payload"]!["SchedulingProvenance"]!;
        if (profile == "metadata-scope") provenance["Metadata"]![RuntimeMetadataKeys.FlowchartExecutionScopeId] = "foreign";
        else if (profile == "work-scope") rootWork["ExecutionScopeId"] = "foreign";
        else provenance[profile] = "foreign";
        await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () => await Checkpoints(harness).HandleAsync(
            Copy(work, JsonSerializer.SerializeToElement(json))));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_first_checkpoint_replay_after_closure_or_completion_is_a_noop_but_changed_wire_conflicts(bool completed)
    {
        var owner = new Owner();
        await using var harness = Build(owner, PersistenceMode.Balanced);
        await Setup(harness, owner);
        var work = await StageCheckpoint(harness, owner);
        await Checkpoints(harness).HandleAsync(work);
        var executions = harness.Services.GetRequiredService<IWorkflowExecutionStateStore>();
        var state = (await executions.FindAsync(harness.ExecutionId))!;
        if (completed) await executions.SaveAsync(state with { Status = WorkflowExecutionStatus.Completed,
            RootVariableFrame = state.RootVariableFrame!.Close(state.RootVariableFrame.Revision) });
        await harness.Services.GetRequiredService<IWorkflowTestScopeStore>().CloseAsync(new(owner.Entry!.Scope.ScopeId,
            WorkflowTestScopeCloseReason.ExplicitTeardown, owner.Clock.Now));
        owner.Clock.Now = owner.Entry.Scope.ExpiresAt;
        var before = JsonSerializer.Serialize(await executions.FindAsync(harness.ExecutionId));
        await Checkpoints(harness).HandleAsync(work);
        Assert.Equal(before, JsonSerializer.Serialize(await executions.FindAsync(harness.ExecutionId)));
        var json = JsonNode.Parse(work.Payload!.Value.GetRawText())!; json["CorrelationId"] = "different";
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(async () =>
            await Checkpoints(harness).HandleAsync(Copy(work, JsonSerializer.SerializeToElement(json))));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }

    [Fact]
    public async Task A_different_first_checkpoint_cannot_restart_an_already_admitted_root()
    {
        var owner = new Owner(); await using var harness = Build(owner, PersistenceMode.Balanced);
        await Setup(harness, owner); var work = await StageCheckpoint(harness, owner);
        await Checkpoints(harness).HandleAsync(work);
        var before = JsonSerializer.Serialize(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () =>
            await Checkpoints(harness).HandleAsync(Copy(work, work.Payload!.Value, "different-checkpoint")));
        Assert.Equal(before, JsonSerializer.Serialize(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId)));
    }

    private static WorkflowCheckpointSchedulerWorkHandler Checkpoints(WorkflowExecutionHarness harness) =>
        Assert.Single(harness.Services.GetServices<IWorkflowSchedulerWorkHandler>().OfType<WorkflowCheckpointSchedulerWorkHandler>());

    private static async Task<RuntimeSchedulerWorkItem> StageCheckpoint(WorkflowExecutionHarness harness, Owner owner, string id = "start")
    {
        var start = new WorkflowExecutionStartCommandPayload(owner.Executable!.Identity, owner.Executable.Identity.ArtifactId,
            new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), null, null, WorkflowRunKind.TestRun,
            WorkflowExecutableSourceProvenance.From(owner.Source!), null, null, null, owner.Entry!.Scope.Partition, owner.Entry.Authority,
            WorkflowExecutableStartAuthority.LiveReference, 0, owner.Entry.Scope) { TestEntry = owner.Entry };
        var prior = (await harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync(new RuntimeSchedulerWorkQuery(harness.ExecutionId)))
            .Select(x => x.WorkItemId).ToHashSet(StringComparer.Ordinal);
        var work = new RuntimeSchedulerWorkItem(id, harness.ExecutionId, id + "-command", WorkflowExecutionCommandKind.Start,
            "envelope", "key", owner.Clock.Now, owner.Clock.Now, payload: JsonSerializer.SerializeToElement(start));
        await Assert.Single(harness.Services.GetServices<IWorkflowSchedulerWorkHandler>().OfType<WorkflowStartSchedulerWorkHandler>()).HandleAsync(work);
        return Assert.Single(await harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync(new RuntimeSchedulerWorkQuery(harness.ExecutionId)), x => !prior.Contains(x.WorkItemId));
    }

    private static RuntimeSchedulerWorkItem Copy(RuntimeSchedulerWorkItem work, JsonElement payload, string? id = null) => new(
        id ?? work.WorkItemId, work.WorkflowExecutionId, work.CommandId, work.CommandKind, work.EnvelopeId, work.IdempotencyKey,
        work.EnqueuedAt, work.RecordedAt, work.Sequence, payload, work.CommandMetadata, work.EnvelopeMetadata, work.ExecutionScopeId, work.Attempt);

    [Fact]
    public async Task Unqualified_ephemeral_mode_refuses_selected_execution()
    {
        var owner = new Owner(); await using var harness = Build(owner, PersistenceMode.Ephemeral);
        await Setup(harness, owner);
        await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () =>
            await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }

    [Fact]
    public async Task Revoked_context_between_dispatch_and_queued_preparation_invokes_nothing()
    {
        var owner = new Owner(); await using var harness = Build(owner, PersistenceMode.Balanced);
        await Setup(harness, owner); owner.RevokeOnQueuedRead = true;
        var result = await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted, result.CommandDispatch.Status);
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }

    [Fact]
    public async Task Source_expiry_during_root_lease_acquisition_refuses_atomic_admission()
    {
        var owner = new Owner();
        await using var harness = Build(owner, PersistenceMode.Balanced, services =>
            services.Replace(ServiceDescriptor.Scoped<IWorkflowExecutableRootWriteLeaseManager>(sp =>
                new ExpireAtRootLease(owner, ActivatorUtilities.CreateInstance<WorkflowExecutableRootWriteLeaseManager>(sp)))));
        await Setup(harness, owner, shortSource: true);
        _ = await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun);
        Assert.True(owner.LeaseHookRan);
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_first_checkpoints_have_one_atomic_winner_or_reconcile_identical_wire(bool same)
    {
        var owner = new Owner(); await using var harness = Build(owner, PersistenceMode.Balanced);
        await Setup(harness, owner); var firstWork = await StageCheckpoint(harness, owner);
        var secondWork = same ? firstWork : await StageCheckpoint(harness, owner, "other-start");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        owner.BeforeSeed = async () => { if (Interlocked.Increment(ref arrivals) == 2) entered.TrySetResult(); await release.Task; };
        var first = Observe(Checkpoints(harness).HandleAsync(firstWork).AsTask());
        owner.Clock.Now = owner.Clock.Now.AddMilliseconds(100);
        var second = Observe(Checkpoints(harness).HandleAsync(secondWork).AsTask());
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally { release.TrySetResult(); }
        var outcomes = await Task.WhenAll(first, second);
        if (same) Assert.All(outcomes, error => Assert.Null(error));
        else { Assert.Single(outcomes, error => error is null); Assert.Single(outcomes, error => error is WorkflowExecutableStartRejectedException); }
        var committed = Assert.Single(harness.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
        Assert.Single(committed.Commit.StateChanges.PostCommitOutbox);
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));

        static async Task<Exception?> Observe(Task task) { try { await task; return null; } catch (Exception error) { return error; } }
    }

    [Theory]
    [InlineData(WorkflowExecutionCommandKind.Start, 1, false)]
    [InlineData(WorkflowExecutionCommandKind.Start, 1, true)]
    [InlineData(WorkflowExecutionCommandKind.Start, 2, false)]
    [InlineData(WorkflowExecutionCommandKind.Start, 2, true)]
    [InlineData(WorkflowExecutionCommandKind.Checkpoint, 1, false)]
    [InlineData(WorkflowExecutionCommandKind.Checkpoint, 1, true)]
    [InlineData(WorkflowExecutionCommandKind.Checkpoint, 2, false)]
    [InlineData(WorkflowExecutionCommandKind.Checkpoint, 2, true)]
    public async Task Selected_wire_without_an_entry_never_falls_back_to_ordinary_execution(WorkflowExecutionCommandKind kind, int protocol, bool explicitNull)
    {
        var owner = new Owner(); await using var harness = Build(owner, PersistenceMode.Balanced);
        await Setup(harness, owner);
        var checkpoint = await StageCheckpoint(harness, owner);
        await harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>().DequeueAsync(harness.ExecutionId);
        var json = kind == WorkflowExecutionCommandKind.Checkpoint ? JsonNode.Parse(checkpoint.Payload!.Value.GetRawText())! :
            JsonNode.Parse(JsonSerializer.Serialize(new WorkflowExecutionStartCommandPayload(owner.Executable!.Identity,
                owner.Executable.Identity.ArtifactId, new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), null, null,
                WorkflowRunKind.TestRun, WorkflowExecutableSourceProvenance.From(owner.Source!), null, null, null,
                owner.Entry!.Scope.Partition, owner.Entry.Authority, WorkflowExecutableStartAuthority.LiveReference, 0, owner.Entry.Scope)
                { TestEntry = owner.Entry }))!;
        if (explicitNull) json["TestEntry"] = null; else json.AsObject().Remove("TestEntry");
        json["SelectedStartProtocol"] = protocol;
        var metadata = new Dictionary<string, string> { ["runtime.artifactId"] = owner.Executable!.Identity.ArtifactId };
        var envelope = new WorkflowExecutionCommandEnvelope("malformed-envelope", harness.ExecutionId,
            new("malformed-command", harness.ExecutionId, kind, owner.Clock.Now, JsonSerializer.SerializeToElement(json), metadata),
            "malformed", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, owner.Clock.Now, metadata: metadata, partition: owner.Entry!.Scope.Partition);
        var actor = await harness.Services.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(harness.ExecutionId,
            WorkflowExecutionActorActivationReason.Start, owner.Clock.Now, "tester", WorkflowExecutionActorCapabilities.None, partition: owner.Entry.Scope.Partition));
        var result = await actor.EnqueueAsync(envelope);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted, result.Status);
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }

    private static WorkflowExecutionHarness Build(Owner owner, PersistenceMode mode, Action<IServiceCollection>? extra = null) =>
        WorkflowExecutionHarness.Create().WithPersistenceMode(mode).ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(owner.Clock);
            services.AddSingleton<IWorkflowTestEntryContextResolver>(owner);
            services.AddSingleton<IRuntimeTestContextSeedSource>(owner);
            services.AddSingleton<IRuntimeTestPersistenceBoundary>(owner);
            services.AddSingleton<IRuntimeTestExecutionRoutingBoundary>(owner);
            services.AddSingleton<IRuntimeSelectedStartTargetProfile>(owner);
            extra?.Invoke(services);
        }).Build("selected-activity", "second-activity");

    private static async Task Setup(WorkflowExecutionHarness harness, Owner owner, ExecutableNode? target = null, bool shortSource = false)
    {
        var selected = target ?? WorkflowExecutionHarness.NewProbeNode("selected");
        var root = new ExecutableNode("container", "container", typeof(ProbeActivity).FullName!, "1", "test.container", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), childSlots:
            [new("children", [WorkflowExecutionHarness.NewProbeNode("before"), selected, WorkflowExecutionHarness.NewProbeNode("after")])]);
        var original = WorkflowExecutionHarness.NewExecutable(owner.WrapSelected?.Invoke(selected) ?? root, owner.WorkflowVariables);
        var publication = await harness.PublishAsync(original, "ordinary-publication");
        owner.Executable = (await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(original.Identity.ArtifactId))!;
        owner.Source = publication with { SourceReferenceId = "test-source", Scope = WorkflowExecutableReferenceScope.TestRun,
            ActivationId = null, SlotId = null, ExpiresAt = shortSource ? owner.Clock.Now.AddSeconds(1) : owner.Clock.Now.AddMinutes(30) };
        await harness.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(owner.Source);
        var scope = new WorkflowTestScope("selected-scope", (shortSource ? owner.Clock.Now.AddMinutes(10) : owner.Clock.Now.AddSeconds(1)), null, new(WorkflowExecutionPartition.DefaultValue));
        owner.Entry = new("context", "setup-v1", owner.Executable.Identity, "selected", scope, new("tester", "tester"));
        await harness.Services.GetRequiredService<IWorkflowTestScopeStore>().CreateAsync(scope, owner.Clock.Now);
    }

    private static WorkflowExecutionStartDispatchRequest Request(WorkflowExecutionHarness harness, Owner owner) => new(
        owner.Executable!.Identity.ArtifactId, "tester", harness.ExecutionId, null, null, null, null, null, null,
        WorkflowRunKind.TestRun, new WorkflowExecutableSourceSelection(owner.Source!.SourceReferenceId), WorkflowExecutableProvenanceRequirement.RequireLiveReference,
        null, null, null, owner.Entry!.Scope.Partition, owner.Entry.Authority, WorkflowExecutableStartAuthority.LiveReference,
        0, owner.Entry.Scope) { TestEntry = owner.Entry };

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Owner : IWorkflowTestEntryContextResolver, IRuntimeTestContextSeedSource,
        IRuntimeTestPersistenceBoundary, IRuntimeTestExecutionRoutingBoundary, IRuntimeSelectedStartTargetProfile
    {
        internal Clock Clock { get; } = new();
        internal WorkflowExecutable? Executable;
        internal WorkflowExecutableSourceReference? Source;
        internal WorkflowTestEntry? Entry;
        internal int SeedReads, EntryReads;
        internal Func<ExecutableNode, ExecutableNode>? WrapSelected;
        internal RuntimeVariableDeclaration[] WorkflowVariables = [];
        internal Func<WorkflowTestEntry, RuntimeTestContextSeed>? Seed;
        internal Func<ExecutableNode, bool>? QualifyTarget;
        internal bool RevokeOnQueuedRead;
        internal bool AtomicHookRan;
        internal bool LeaseHookRan;
        public ValueTask<WorkflowTestEntry?> FindAsync(string contextId, CancellationToken cancellationToken = default)
        { EntryReads++; return ValueTask.FromResult(RevokeOnQueuedRead && EntryReads > 3 ? null : Entry); }
        internal Func<ValueTask>? BeforeSeed;
        public async ValueTask<RuntimeTestContextSeed?> ResolveAsync(WorkflowTestEntry entry, WorkflowExecutable executable,
            string workflowExecutionId, CancellationToken cancellationToken = default)
        { SeedReads++; if (BeforeSeed is not null) await BeforeSeed(); return Seed?.Invoke(entry) ?? new(entry.ContextId, entry.ContextFingerprint, [], []); }
        public ValueTask<bool> CanExecuteAsync(WorkflowTestEntry entry, WorkflowExecutable executable, ExecutableNode target,
            IRuntimeActivityExecutionPipeline pipeline, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(QualifyTarget?.Invoke(target) ?? (target.ActivityType == ProbeActivity.ProbeActivityType || target.ActivityType == typeof(ProbeActivity).FullName));
        public bool CanPersistTestEntry(RuntimeTestPersistenceBoundaryContext context) => true;
        public ValueTask<bool> CanRouteTestEntryAsync(WorkflowTestEntry entry, IWorkflowExecutionActorProvider actors,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class BeforeCommit(Owner owner, IServiceProvider services, string profile) : IRuntimeCheckpointCommitEnricher
    {
        public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
        {
            if (commit.Checkpoint.Name == RuntimeCheckpointNames.WorkflowStarted && commit.StateChanges.WorkflowExecution?.State.TestEntry is not null)
            {
                owner.AtomicHookRan = true;
                if (profile == "expiry") owner.Clock.Now = owner.Entry!.Scope.ExpiresAt;
                else await services.GetRequiredService<IWorkflowTestScopeStore>().CloseAsync(new(owner.Entry!.Scope.ScopeId,
                    WorkflowTestScopeCloseReason.ExplicitTeardown, owner.Clock.Now), cancellationToken);
            }
            return commit;
        }
    }
}
