using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

public sealed class RecurringTriggerScheduleIndexerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Index_MaterializesEverySchedule_BeforeInnerAndReplacementWrites()
    {
        var events = new List<string>();
        var inner = new FakeInner(() => events.Add("inner"));
        var store = new RecordingScheduleStore(events);
        var calculator = new RecordingCalculator(events);
        var indexer = CreateIndexer(
            inner,
            store,
            calculator,
            new FakeScheduleProvider("Elsa.Timer", "Timer", "hash-1", "PT5M"),
            new FakeScheduleProvider("Elsa.Cron", "Cron", "hash-2", "0 * * * *", RecurringScheduleKind.Cron));

        var root = PlainNode("root", "Elsa.Sequence",
            TriggerNode("timer", "Elsa.Timer"),
            TriggerNode("cron", "Elsa.Cron"));
        await indexer.IndexAsync(Executable("artifact-1", root));

        Assert.Equal(["calculate:Cron", "calculate:Interval", "inner", "delete", "save:Cron", "save:Timer"], events);
    }

    [Fact]
    public async Task Index_IgnoresNonTriggerNodes()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        var indexer = CreateIndexer(new FakeInner(), store, new FakeScheduleProvider("Elsa.Timer", "Timer", "hash-1", "PT5M"));

        await indexer.IndexAsync(Executable("artifact-1", PlainNode("node-1", "Elsa.Timer")));

        Assert.Empty(await store.ListDueAsync(Now.AddMinutes(10), 10));
    }

    [Fact]
    public async Task Index_ReplacesPriorSchedulesForArtifact_OnRepublish()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        // Seed a stale schedule the current publish no longer contains.
        await store.SaveAsync(new RecurringTriggerSchedule(
            RecurringTriggerSchedule.BuildId("artifact-1", "old-node"), "artifact-1", "Timer", "old", RecurringScheduleKind.Interval, "PT9M", Now, Now));
        var indexer = CreateIndexer(new FakeInner(), store, new FakeScheduleProvider("Elsa.Timer", "Timer", "hash-1", "PT5M"));

        await indexer.IndexAsync(Executable("artifact-1", TriggerNode("node-1", "Elsa.Timer")));

        var schedule = Assert.Single(await store.ListDueAsync(Now.AddMinutes(10), 10));
        Assert.Equal(RecurringTriggerSchedule.BuildId("artifact-1", "node-1"), schedule.ScheduleId);
    }

    [Fact]
    public async Task Index_ExhaustedCron_FailsBeforeInnerAndPreservesSeededBindingsAndSchedules()
    {
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        var priorBinding = Binding("artifact-1", "old-node", "old-hash");
        await bindingStore.SaveAsync(priorBinding);
        var replacementBinding = Binding("artifact-1", "node-1", "new-hash");
        var inner = new StoreBackedInner(bindingStore, replacementBinding);
        var scheduleStore = new InMemoryRecurringTriggerScheduleStore();
        var priorSchedule = Schedule("artifact-1", "old-node", "old-hash");
        await scheduleStore.SaveAsync(priorSchedule);
        var indexer = CreateIndexer(inner, scheduleStore,
            new FakeScheduleProvider("Elsa.Cron", "Cron", "hash-cron", "0 0 30 2 *", RecurringScheduleKind.Cron));

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(async () =>
            await indexer.IndexAsync(Executable("artifact-1", TriggerNode("node-1", "Elsa.Cron"))));

        Assert.Equal("artifact-1", exception.ArtifactId);
        Assert.Equal("node-1", exception.ExecutableNodeId);
        Assert.False(inner.Called);
        Assert.Equal([priorBinding], await bindingStore.ListByArtifactAsync("artifact-1"));
        Assert.Equal(priorSchedule, await scheduleStore.FindAsync(priorSchedule.ScheduleId));
    }

    [Fact]
    public async Task Index_InvalidLaterNode_FailsBeforeInnerAndPreservesPriorSchedule()
    {
        var inner = new FakeInner();
        var store = new InMemoryRecurringTriggerScheduleStore();
        var priorSchedule = Schedule("artifact-1", "old-node", "old-hash");
        await store.SaveAsync(priorSchedule);
        var indexer = CreateIndexer(inner, store,
            new FakeScheduleProvider("Elsa.Timer", "Timer", "timer-hash", "PT5M"),
            new FakeScheduleProvider("Elsa.Cron", "Cron", "cron-hash", "not-a-cron", RecurringScheduleKind.Cron));
        var root = PlainNode("root", "Elsa.Sequence",
            TriggerNode("invalid-later", "Elsa.Cron"),
            TriggerNode("valid-first", "Elsa.Timer"));

        await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(async () =>
            await indexer.IndexAsync(Executable("artifact-1", root)));

        Assert.False(inner.Called);
        Assert.Equal(priorSchedule, await store.FindAsync(priorSchedule.ScheduleId));
        Assert.Null(await store.FindAsync(RecurringTriggerSchedule.BuildId("artifact-1", "valid-first")));
    }

    [Fact]
    public async Task Index_InnerFailure_LeavesSchedulesUnchanged()
    {
        var expected = new InvalidOperationException("inner failed");
        var inner = new FakeInner(exception: expected);
        var store = new InMemoryRecurringTriggerScheduleStore();
        var priorSchedule = Schedule("artifact-1", "old-node", "old-hash");
        await store.SaveAsync(priorSchedule);
        var indexer = CreateIndexer(inner, store,
            new FakeScheduleProvider("Elsa.Timer", "Timer", "new-hash", "PT5M"));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await indexer.IndexAsync(Executable("artifact-1", TriggerNode("node-1", "Elsa.Timer"))));

        Assert.Same(expected, actual);
        Assert.Equal(priorSchedule, await store.FindAsync(priorSchedule.ScheduleId));
        Assert.Null(await store.FindAsync(RecurringTriggerSchedule.BuildId("artifact-1", "node-1")));
    }

    [Fact]
    public async Task Index_WithNoProviders_IsPassthrough()
    {
        var inner = new FakeInner();
        var store = new InMemoryRecurringTriggerScheduleStore();
        var indexer = new RecurringTriggerScheduleIndexer(
            inner, [], store, new RecurringScheduleCalculator(), new FixedClock(Now),
            NullLogger<RecurringTriggerScheduleIndexer>.Instance);

        await indexer.IndexAsync(Executable("artifact-1", TriggerNode("node-1", "Elsa.Timer")));

        Assert.True(inner.Called);
        Assert.Empty(await store.ListDueAsync(Now.AddMinutes(10), 10));
    }

    private static RecurringTriggerScheduleIndexer CreateIndexer(
        IWorkflowTriggerIndexer inner,
        IRecurringTriggerScheduleStore store,
        params IRecurringTriggerScheduleProvider[] providers) =>
        CreateIndexer(inner, store, new RecurringScheduleCalculator(), providers);

    private static RecurringTriggerScheduleIndexer CreateIndexer(
        IWorkflowTriggerIndexer inner,
        IRecurringTriggerScheduleStore store,
        IRecurringScheduleCalculator calculator,
        params IRecurringTriggerScheduleProvider[] providers) =>
        new(inner, providers, store, calculator, new FixedClock(Now),
            NullLogger<RecurringTriggerScheduleIndexer>.Instance);

    private static WorkflowTriggerBinding Binding(string artifactId, string nodeId, string stimulusHash) =>
        new(WorkflowTriggerBinding.BuildId(artifactId, nodeId, stimulusHash), artifactId, "definition-1", "1.0.0", "sha256:v1",
            nodeId, "Cron", stimulusHash, null, new Dictionary<string, string>(), Now);

    private static RecurringTriggerSchedule Schedule(string artifactId, string nodeId, string stimulusHash) =>
        new(RecurringTriggerSchedule.BuildId(artifactId, nodeId), artifactId, "Cron", stimulusHash,
            RecurringScheduleKind.Cron, "0 * * * *", Now.AddHours(1), Now);

    private static WorkflowExecutable Executable(string artifactId, ExecutableNode root) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", "sha256:v1"),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UnixEpoch,
            compatibilityMetadata: new Dictionary<string, string>());

    private static ExecutableNode TriggerNode(string nodeId, string activityType, params ExecutableNode[] children) =>
        Node(nodeId, activityType, new Dictionary<string, string> { [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType }, children);

    private static ExecutableNode PlainNode(string nodeId, string activityType, params ExecutableNode[] children) =>
        Node(nodeId, activityType, new Dictionary<string, string>(), children);

    private static ExecutableNode Node(string nodeId, string activityType, Dictionary<string, string> metadata, ExecutableNode[] children)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: metadata,
            childSlots: children.Length == 0 ? [] : [new ExecutableChildSlot("Body", children)]);
    }

    private sealed class FakeInner(Action? onCalled = null, Exception? exception = null) : IWorkflowTriggerIndexer
    {
        public bool Called { get; private set; }

        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
        {
            Called = true;
            onCalled?.Invoke();
            if (exception is not null)
                return ValueTask.FromException<IReadOnlyCollection<WorkflowTriggerBinding>>(exception);
            return new ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>>(Array.Empty<WorkflowTriggerBinding>());
        }
    }

    private sealed class StoreBackedInner(IWorkflowTriggerBindingStore store, WorkflowTriggerBinding replacement) : IWorkflowTriggerIndexer
    {
        public bool Called { get; private set; }

        public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
        {
            Called = true;
            await store.DeleteByArtifactAsync(executable.Identity.ArtifactId, cancellationToken);
            await store.SaveAsync(replacement, cancellationToken);
            return [replacement];
        }
    }

    private sealed class RecordingCalculator(List<string> events) : IRecurringScheduleCalculator
    {
        private readonly RecurringScheduleCalculator _inner = new();

        public DateTimeOffset? ComputeNext(RecurringScheduleKind kind, string expression, DateTimeOffset after)
        {
            events.Add($"calculate:{kind}");
            return _inner.ComputeNext(kind, expression, after);
        }
    }

    private sealed class RecordingScheduleStore(List<string> events) : IRecurringTriggerScheduleStore
    {
        private readonly InMemoryRecurringTriggerScheduleStore _inner = new();

        public ValueTask<RecurringTriggerSchedule> SaveAsync(RecurringTriggerSchedule schedule, CancellationToken cancellationToken = default)
        {
            events.Add($"save:{schedule.StimulusType}");
            return _inner.SaveAsync(schedule, cancellationToken);
        }

        public ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default) => _inner.ListDueAsync(asOf, limit, cancellationToken);
        public ValueTask<RecurringTriggerSchedule?> FindAsync(string scheduleId, CancellationToken cancellationToken = default) => _inner.FindAsync(scheduleId, cancellationToken);
        public ValueTask<bool> TryAdvanceAsync(string scheduleId, DateTimeOffset expectedNextOccurrence, DateTimeOffset newNextOccurrence, CancellationToken cancellationToken = default) => _inner.TryAdvanceAsync(scheduleId, expectedNextOccurrence, newNextOccurrence, cancellationToken);

        public ValueTask DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
        {
            events.Add("delete");
            return _inner.DeleteByArtifactAsync(artifactId, cancellationToken);
        }

        public ValueTask DeleteAsync(string scheduleId, CancellationToken cancellationToken = default) => _inner.DeleteAsync(scheduleId, cancellationToken);
    }

    private sealed class FakeScheduleProvider(
        string activityType,
        string stimulusType,
        string stimulusHash,
        string expression,
        RecurringScheduleKind kind = RecurringScheduleKind.Interval) : IRecurringTriggerScheduleProvider
    {
        public RecurringScheduleDescriptor? Describe(ExecutableNode node) =>
            StringComparer.Ordinal.Equals(node.ActivityType, activityType)
                ? new RecurringScheduleDescriptor(stimulusType, stimulusHash, kind, expression)
                : null;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
