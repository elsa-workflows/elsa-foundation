using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// The input paths of research R3a that activation-time secret resolution owns (spec 188, T091): structural parent
/// evaluation (IP5), child-completion re-materialization (IP6), the boundary retry (IP9) and the operator reschedule
/// (IP10), whose successor is invoked through the real scheduler work handlers. Each path that activates a CLR activity
/// resolves the reference again, and none persists a value. Invoke and
/// bookmark resume (IP3, IP4) are covered by <see cref="ClrActivityActivatorTests"/> and
/// <see cref="WorkflowInvokeActivitySchedulerWorkHandlerTests"/>.
/// </summary>
public sealed class SecretInputPathCoverageTests
{
    private const string ParentNodeId = "node-parent";
    private const string WaitNodeId = "node-wait";
    private readonly FakeRuntimeSecretResolver _resolver = new();
    private readonly SecretValueRecorder _recorder = new();

    public SecretInputPathCoverageTests() => _resolver.RespondWithCallSequence();

    [Fact]
    public async Task IP5_structural_parent_evaluation_resolves_again_on_the_notify_parent_and_parent_completion_paths()
    {
        await using var harness = NewHarness(
            ["actexec-parent", "actexec-child", "actexec-leaf"],
            services => services.AddSingleton(new ParentNotificationDirective { InvokeCodes = ["escalate"] }));

        var run = await harness.RunAsync(StructuralExecutionTestSupport.NewExecutable(
            SecretResolutionTestSupport.NewSecretNode(ParentNodeId, typeof(SecretReadingParentActivity),
                StructuralExecutionTestSupport.NewStructuralNode("node-child", typeof(NotifyingStructuralChildActivity),
                    WorkflowExecutionHarness.NewProbeNode("node-leaf")))));

        run.AssertWorkflowCompleted();
        // Each evaluation re-activates the parent from its committed snapshot, and each activation resolves anew.
        Assert.Equal(
            [
                (SecretValueRecorder.Execute, FakeRuntimeSecretResolver.SequenceValue(1)),
                (SecretValueRecorder.ChildNotified, FakeRuntimeSecretResolver.SequenceValue(2)),
                (SecretValueRecorder.ChildCompleted, FakeRuntimeSecretResolver.SequenceValue(3))
            ],
            _recorder.Entries);
        Assert.Equal(3, _resolver.Requests.Count);
        SecretResolutionTestSupport.AssertSnapshotWithheld(run.State(ParentNodeId));
        await SecretResolutionTestSupport.AssertNotPersistedAsync(harness, _recorder.Values);
    }

    [Fact]
    public async Task IP6_child_completion_rematerializes_a_withheld_envelope_and_resolves_it_again_without_persisting_it()
    {
        await using var harness = NewHarness(["actexec-parent", "actexec-leaf"]);

        var run = await harness.RunAsync(StructuralExecutionTestSupport.NewExecutable(
            SecretResolutionTestSupport.NewSecretNode(ParentNodeId, typeof(SecretRematerializingParentActivity),
                WorkflowExecutionHarness.NewProbeNode("node-leaf"))));

        run.AssertWorkflowCompleted();
        // The child-completion evaluation activates the parent on its committed snapshot (resolution 2), then again on
        // the re-materialized snapshot (resolution 3), whose activity runs the callback.
        Assert.Equal(
            [
                (SecretValueRecorder.Execute, FakeRuntimeSecretResolver.SequenceValue(1)),
                (SecretValueRecorder.ChildCompleted, FakeRuntimeSecretResolver.SequenceValue(3))
            ],
            _recorder.Entries);
        Assert.Equal(3, _resolver.Requests.Count);
        SecretResolutionTestSupport.AssertSnapshotWithheld(run.State(ParentNodeId));
        await SecretResolutionTestSupport.AssertNotPersistedAsync(
            harness,
            [.. Enumerable.Range(1, 3).Select(FakeRuntimeSecretResolver.SequenceValue)]);
    }

    [Fact]
    public async Task IP9_a_boundary_retry_clones_only_boundary_inputs_and_neither_activates_nor_resolves()
    {
        const string boundaryId = "boundary";
        const string retryId = "boundary-retry";
        var activator = new RecordingActivityActivator();
        await using var harness = NewHarness([], services => services.AddScoped<IActivityActivator>(_ => activator));
        var services = harness.Services;
        var identity = WorkflowExecutionHarness.Identity;
        await services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(WorkflowExecutionHarness.NewExecutable(
            SecretResolutionTestSupport.NewSecretNode(ParentNodeId, typeof(SecretReadingParentActivity))));
        await services.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(FaultedBoundary(boundaryId, identity));
        var durableValues = services.GetRequiredService<IDurableValueStateStore>();
        var order = JsonSerializer.SerializeToElement(new { id = 42 });
        await durableValues.SaveAsync(BoundaryValue(boundaryId, "input", order));
        await durableValues.SaveAsync(BoundaryValue(boundaryId, "output", JsonSerializer.SerializeToElement(new { total = 7 })));

        await services.GetServices<IWorkflowSchedulerWorkHandler>()
            .Single(handler => handler is WorkflowRetryActivityBoundarySchedulerWorkHandler)
            .HandleAsync(RetryWorkItem(boundaryId, retryId, identity));

        var cloned = Assert.Single(
            await durableValues.ListAllDurableValueStatesAsync(harness.ExecutionId),
            value => StringComparer.Ordinal.Equals(value.SourceActivityExecutionId, retryId));
        Assert.Equal("input", cloned.Metadata[RuntimeMetadataKeys.BoundaryValueRole]);
        // The clone is the boundary's own input, not the boundary's withheld snapshot envelope.
        var clonedJson = JsonSerializer.Serialize(cloned);
        Assert.DoesNotContain(SecretResolutionTestSupport.ReferenceName, clonedJson, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ValuePresence.Withheld), clonedJson, StringComparison.Ordinal);
        Assert.True(JsonElement.DeepEquals(order, cloned.InlineValue!.Value));
        var commit = Assert.Single(services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits()).Commit;
        // No activity state, so not the boundary's withheld snapshot either, is carried over: the retried execution
        // materializes a fresh snapshot when its scheduled work runs.
        Assert.Empty(commit.StateChanges.ActivityExecutions);
        var scheduled = Assert.Single(commit.PostCommitIntents).Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, scheduled.CommandKind);
        Assert.Empty(activator.Requests);
        Assert.Empty(_resolver.Requests);
    }

    [Fact]
    public async Task IP10_an_operator_reschedule_copies_the_withheld_envelope_and_the_successor_resolves_again()
    {
        await using var harness = NewHarness(["actexec-wait"]);
        var executable = SecretResolutionTestSupport.NewWaitingExecutable(WaitNodeId);
        var source = (await harness.RunAsync(executable)).State(WaitNodeId);
        Assert.Equal(ActivityExecutionStatus.Suspended, source.Status);
        await using var scope = harness.Services.CreateAsyncScope();
        var workflow = (await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!;
        var staging = new Staging();

        var preflight = await scope.ServiceProvider.GetRequiredService<RescheduleActivityAlterationHandler>().PreflightAsync(
            new WorkflowAlterationPreflightContext(
                new WorkflowAlterationJobState("job", "plan", harness.ExecutionId, WorkflowExecutionPartition.DefaultValue, 0, WorkflowAlterationJobStatus.Running,
                    new WorkflowAlterationJobClaim("worker", "claim", DateTimeOffset.MaxValue), 1, [], null, null, DateTimeOffset.UnixEpoch, null, null, 0),
                new WorkflowAlterationEnvelope("RescheduleActivity", 1, JsonSerializer.SerializeToElement(new { sourceActivityExecutionId = source.Execution.ActivityExecutionId })),
                0,
                new WorkflowAlterationProjectedState([workflow, new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection([source])]),
                staging));

        Assert.True(preflight.IsAccepted, preflight.Failure?.Message);
        var builder = new WorkflowAlterationRuntimeCheckpointStateBuilder(workflow);
        Assert.IsAssignableFrom<IWorkflowAlterationRuntimeCheckpointStagedChange>(Assert.Single(staging.StagedChanges)).Apply(builder);
        var successor = Assert.Single(builder.ActivityExecutions, change => change.State.Status == ActivityExecutionStatus.Running).State;
        Assert.Same(source.InputSnapshot, successor.InputSnapshot);
        SecretResolutionTestSupport.AssertSnapshotWithheld(successor);

        // Commit the reschedule's activity states, then deliver its continuation through the real scheduler work
        // handlers: the successor's start enqueues its invoke, whose activation resolves the reference again.
        var activityStates = scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>();
        foreach (var change in builder.ActivityExecutions)
            await activityStates.SaveAsync(change.State);
        await DispatchAsync(scope.ServiceProvider, Assert.Single(builder.PostCommitIntents).MaterializedSchedulerWorkItem!);

        Assert.Equal(
            [
                (SecretValueRecorder.Execute, FakeRuntimeSecretResolver.SequenceValue(1)),
                (SecretValueRecorder.Execute, FakeRuntimeSecretResolver.SequenceValue(2))
            ],
            _recorder.Entries);
        Assert.Equal(2, _resolver.Requests.Count);
        var invoked = (await activityStates.FindAsync(harness.ExecutionId, successor.Execution.ActivityExecutionId))!;
        Assert.Equal(ActivityExecutionStatus.Suspended, invoked.Status);
        SecretResolutionTestSupport.AssertSnapshotWithheld(invoked);
        await SecretResolutionTestSupport.AssertNotPersistedAsync(harness, _recorder.Values);
    }

    /// <summary>
    /// Runs <paramref name="workItem"/> and then every item it queues through the registered scheduler work handlers,
    /// choosing a handler as the scheduler drainer does: a specific handler before a fallback one.
    /// </summary>
    private static async Task DispatchAsync(IServiceProvider services, RuntimeSchedulerWorkItem workItem)
    {
        var handlers = services.GetServices<IWorkflowSchedulerWorkHandler>().OrderBy(handler => handler is IFallbackWorkflowSchedulerWorkHandler).ToArray();
        var queue = services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        for (RuntimeSchedulerWorkItem? item = workItem; item is not null; item = await queue.DequeueAsync(workItem.WorkflowExecutionId))
            await handlers.First(handler => handler.CanHandle(item)).HandleAsync(item);
    }

    private WorkflowExecutionHarness NewHarness(IReadOnlyCollection<string> activityExecutionIds, Action<IServiceCollection>? configure = null) =>
        SecretResolutionTestSupport.NewHarness(_resolver, _recorder, activityExecutionIds, configure);

    /// <summary>A faulted activity-scope boundary whose committed snapshot holds a withheld secret reference.</summary>
    private static ActivityExecutionState FaultedBoundary(string id, WorkflowExecutableIdentity identity) =>
        new(
            new ActivityExecution(id, WorkflowExecutionHarness.WorkflowExecutionId, ParentNodeId, $"authored-{ParentNodeId}", "activity", "1"),
            ActivityExecutionStatus.Faulted,
            null,
            1,
            WorkflowExecutionHarness.Timestamp,
            WorkflowExecutionHarness.Timestamp,
            WorkflowExecutionHarness.Timestamp,
            null,
            null,
            null,
            null,
            ActivitySchedulingProvenance.From(WorkflowExecutionHarness.WorkflowExecutionId, null, null, null, null, null, id, "test"),
            null,
            [],
            [],
            1,
            1,
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.PinnedArtifactId] = identity.ArtifactId,
                [RuntimeMetadataKeys.PinnedArtifactVersion] = identity.ArtifactVersion,
                [RuntimeMetadataKeys.PinnedArtifactHash] = identity.ArtifactHash
            },
            ExecutionScopeId: id,
            Attempt: new ActivityExecutionAttemptLineage(1, id, null))
        {
            InputSnapshot = new ActivityInputSnapshot(
                id,
                "contract",
                "bindings",
                new Dictionary<string, ValueEnvelope> { [SecretResolutionTestSupport.InputKey] = SecretResolutionTestSupport.Withheld() },
                WorkflowExecutionHarness.Timestamp)
        };

    private static DurableValueState BoundaryValue(string executionId, string role, JsonElement value)
    {
        var id = $"{executionId}:{role}:order";
        return new DurableValueState(
            id,
            WorkflowExecutionHarness.WorkflowExecutionId,
            id,
            new RuntimeValueTypeDescriptor("object", "elsa.json", JsonSerializer.SerializeToElement(new { alias = "object" })),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            value,
            null,
            executionId,
            WorkflowExecutionHarness.Timestamp,
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.BoundaryExecutionScopeId] = executionId,
                [RuntimeMetadataKeys.BoundaryValueRole] = role,
                [RuntimeMetadataKeys.BoundaryReferenceKey] = "order",
                [RuntimeMetadataKeys.BoundaryInputName] = "Order",
                ["runtime.storageDriverKey"] = "elsa.json"
            });
    }

    private static RuntimeSchedulerWorkItem RetryWorkItem(string boundaryId, string retryId, WorkflowExecutableIdentity identity) =>
        new(
            "work-retry",
            WorkflowExecutionHarness.WorkflowExecutionId,
            "command-retry",
            WorkflowExecutionCommandKind.RetryActivityBoundary,
            "envelope",
            "key-retry",
            WorkflowExecutionHarness.Timestamp,
            WorkflowExecutionHarness.Timestamp,
            1,
            JsonSerializer.SerializeToElement(new RetryActivityBoundaryCommand(boundaryId, retryId, identity, "policy-retry")),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            boundaryId);

    /// <summary>An activator that records every request and refuses it: the path under test must not activate anything.</summary>
    private sealed class RecordingActivityActivator : IActivityActivator
    {
        private readonly List<ActivityActivationRequest> _requests = [];

        public IReadOnlyList<ActivityActivationRequest> Requests
        {
            get
            {
                lock (_requests)
                    return _requests.ToArray();
            }
        }

        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationRequest request, CancellationToken cancellationToken = default)
        {
            lock (_requests)
                _requests.Add(request);
            throw new InvalidOperationException("This path must not activate an activity.");
        }
    }

    private sealed class Staging : IWorkflowAlterationStagingWorkspace
    {
        public List<IWorkflowAlterationStagedChange> StagedChanges { get; } = [];
        IReadOnlyList<IWorkflowAlterationStagedChange> IWorkflowAlterationStagingWorkspace.StagedChanges => StagedChanges;
        public void Stage(IWorkflowAlterationStagedChange change) => StagedChanges.Add(change);
    }
}
