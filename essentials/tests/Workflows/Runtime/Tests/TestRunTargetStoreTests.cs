using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class TestRunTargetStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Driver_action_accepts_api_root_relative_paths()
    {
        var action = new TestRunDriverAction("http-request", "/workflows/test-runs/target", "POST");

        Assert.Equal("/workflows/test-runs/target", action.RelativeUrl);
    }

    [Theory]
    [InlineData("https://example.test/fire")]
    [InlineData("//example.test/fire")]
    [InlineData("\\\\example.test\\fire")]
    [InlineData("/workflows\\fire")]
    [InlineData("/workflows/../fire")]
    [InlineData("/workflows/%2e%2e/fire")]
    public void Driver_action_rejects_external_or_traversing_paths(string relativeUrl)
    {
        Assert.Throws<ArgumentException>(() =>
            new TestRunDriverAction("http-request", relativeUrl, "POST"));
    }

    [Fact]
    public async Task BeginDispatch_is_idempotent_for_same_data_and_refuses_changed_data()
    {
        var clock = new FakeTimeProvider(Now);
        ITestRunTargetStore store = new MemoryTestRunTargetStore(clock);
        var target = NewTarget();
        await store.SaveAsync(target);

        var first = await store.BeginDispatchAsync(target.TestRunId, "digest-one");
        var retry = await store.BeginDispatchAsync(target.TestRunId, "digest-one");
        var changed = await store.BeginDispatchAsync(target.TestRunId, "digest-two");

        Assert.Equal(TestRunTargetDisposition.Ready, first.Disposition);
        Assert.Equal(TestRunTargetState.Dispatching, first.Target!.State);
        Assert.Equal(TestRunTargetDisposition.Ready, retry.Disposition);
        Assert.Equal(TestRunTargetDisposition.PayloadConflict, changed.Disposition);
    }

    [Fact]
    public async Task Begin_dispatch_expires_with_the_existing_test_scope_but_retains_cleanup_state()
    {
        var clock = new FakeTimeProvider(Now);
        ITestRunTargetStore store = new MemoryTestRunTargetStore(clock);
        var target = NewTarget();
        await store.SaveAsync(target);

        clock.Advance(TimeSpan.FromMinutes(30));

        Assert.NotNull(await store.FindAsync(target.TestRunId));
        var expired = await store.BeginDispatchAsync(target.TestRunId, "digest-one");
        Assert.Equal(TestRunTargetDisposition.Expired, expired.Disposition);
        Assert.Equal(target.Scope.ExpiresAt, target.ExpiresAt);
    }

    [Fact]
    public async Task Stopped_target_cannot_begin_dispatch()
    {
        var store = new MemoryTestRunTargetStore(new FakeTimeProvider(Now));
        var target = NewTarget();
        await store.SaveAsync(target);

        await store.MarkStoppedAsync(target.TestRunId);
        var admission = await store.BeginDispatchAsync(target.TestRunId, "digest-one");

        Assert.Equal(TestRunTargetDisposition.Stopped, admission.Disposition);
    }

    [Fact]
    public async Task Bounded_target_store_recovers_capacity_after_scope_cleanup_removes_targets()
    {
        var store = new MemoryTestRunTargetStore(new FakeTimeProvider(Now));
        for (var index = 0; index < MemoryTestRunTargetStore.MaximumTargets; index++)
            await store.SaveAsync(NewTarget($"test-run-{index}"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveAsync(NewTarget("test-run-over-capacity")).AsTask());

        await store.RemoveAsync("test-run-0");
        await store.SaveAsync(NewTarget("test-run-replacement"));
    }

    [Fact]
    public async Task Root_canceller_uses_ordinary_mailbox_and_does_not_cancel_terminal_runs()
    {
        var clock = new FakeTimeProvider(Now);
        var partition = new WorkflowExecutionPartition("partition-eu");
        var executionStore = new InMemoryWorkflowExecutionStateStore();
        var identity = new WorkflowExecutableIdentity("artifact-1", "definition-1", "draft:snapshot-1", "draft", "sha256:artifact");
        await executionStore.SaveAsync(new WorkflowExecutionState(
            "execution-1",
            identity,
            WorkflowExecutionStatus.Running,
            null,
            Now,
            Now,
            Now,
            null,
            null,
            null,
            null,
            new Dictionary<string, string>())
        {
            RunKind = WorkflowRunKind.TestRun,
            Partition = partition
        });
        var actor = new RecordingActor(Now);
        var canceller = new TestRunRootCanceller(
            executionStore,
            new RecordingActorProvider(actor),
            new ShortRuntimeExecutionIdGenerator(clock),
            clock);

        var requested = await canceller.RequestCancelAsync("test-run-1", "execution-1", partition);

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, requested?.Status);
        var envelope = Assert.IsType<WorkflowExecutionCommandEnvelope>(actor.Envelope);
        Assert.Equal(WorkflowExecutionCommandKind.Cancel, envelope.Command.Kind);
        Assert.Equal(WorkflowExecutionCommandDeliveryMode.AtLeastOnce, envelope.DeliveryMode);
        Assert.Equal("workflow-test-run:test-run-1:cancel", envelope.IdempotencyKey);
        Assert.Equal(partition, envelope.Partition);

        await executionStore.SaveAsync((await executionStore.FindAsync("execution-1"))! with
        {
            Status = WorkflowExecutionStatus.Completed,
            CompletedAt = Now.AddSeconds(1)
        });
        actor.Envelope = null;

        Assert.Null(await canceller.RequestCancelAsync("test-run-1", "execution-1", partition));
        Assert.Null(actor.Envelope);
    }

    private static TestRunTarget NewTarget(string testRunId = "test-run-1")
    {
        var scope = new WorkflowTestScope(testRunId, Now.AddMinutes(30), "tenant-1", new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue));
        var executable = new WorkflowExecutableIdentity("artifact-1", "definition-1", "draft:snapshot-1", "draft", "sha256:artifact");
        var binding = new WorkflowTriggerBinding(
            "binding-1", executable.ArtifactId, executable.DefinitionId, executable.ArtifactVersion, executable.ArtifactHash,
            "node-1", "Event", "sha256:event", null, new Dictionary<string, string>(), Now);
        return new TestRunTarget(
            scope.ScopeId,
            "driver.event",
            "Elsa.Event",
            "fire-now",
            executable,
            "reference-1",
            binding,
            scope,
            "workflow-designer-test-run",
            $"execution-{testRunId}",
            $"workflow-test-run:{testRunId}:start",
            new Dictionary<string, System.Text.Json.JsonElement>(),
            new Dictionary<string, object?>(),
            driverState: null,
            createdAt: Now);
    }

    private sealed class RecordingActor(DateTimeOffset now) : IWorkflowExecutionActor
    {
        public WorkflowExecutionCommandEnvelope? Envelope { get; set; }
        public WorkflowExecutionActorDescriptor Descriptor { get; } = new(
            "execution-1", "agent-1", "test-provider", WorkflowExecutionActorStatus.Active,
            WorkflowExecutionActorCapabilities.InProcessMailbox, now);

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(
            WorkflowExecutionCommandEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Envelope = envelope;
            return ValueTask.FromResult(new WorkflowExecutionCommandDispatchResult(
                envelope.EnvelopeId,
                envelope.WorkflowExecutionId,
                WorkflowExecutionCommandDispatchStatus.Accepted,
                now));
        }
    }

    private sealed class RecordingActorProvider(RecordingActor actor) : IWorkflowExecutionActorProvider
    {
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.InProcessMailbox;

        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(
            WorkflowExecutionActorActivationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IWorkflowExecutionActor>(actor);

        public ValueTask PassivateAsync(
            WorkflowExecutionActorPassivationRequest request,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
