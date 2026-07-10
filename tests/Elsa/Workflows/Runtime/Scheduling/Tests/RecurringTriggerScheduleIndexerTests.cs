using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

public sealed class RecurringTriggerScheduleIndexerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Index_RunsInnerFirst_ThenWritesScheduleForTriggerNode()
    {
        var inner = new FakeInner();
        var store = new InMemoryRecurringTriggerScheduleStore();
        var indexer = CreateIndexer(inner, store, new FakeScheduleProvider("Elsa.Timer", "Timer", "hash-1", "PT5M"));

        var bindings = await indexer.IndexAsync(Executable("artifact-1", TriggerNode("node-1", "Elsa.Timer")));

        // Inner ran (its bindings are returned) and one schedule was written from the same node.
        Assert.True(inner.Called);
        Assert.Empty(bindings);
        var schedule = Assert.Single(await store.ListDueAsync(Now.AddMinutes(10), 10));
        Assert.Equal(RecurringTriggerSchedule.BuildId("artifact-1", "node-1"), schedule.ScheduleId);
        Assert.Equal("Timer", schedule.StimulusType);
        Assert.Equal(Now.AddMinutes(5), schedule.NextOccurrence);
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
    public async Task Index_SkipsExhaustedCronNode_WithoutWritingSchedule()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        // Feb 30 never occurs -> ComputeNext returns null -> no schedule persisted.
        var indexer = CreateIndexer(new FakeInner(), store,
            new FakeScheduleProvider("Elsa.Cron", "Cron", "hash-cron", "0 0 30 2 *", RecurringScheduleKind.Cron));

        await indexer.IndexAsync(Executable("artifact-1", TriggerNode("node-1", "Elsa.Cron")));

        Assert.Empty(await store.ListDueAsync(Now.AddYears(5), 10));
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
        FakeInner inner,
        IRecurringTriggerScheduleStore store,
        IRecurringTriggerScheduleProvider provider) =>
        new(inner, [provider], store, new RecurringScheduleCalculator(), new FixedClock(Now),
            NullLogger<RecurringTriggerScheduleIndexer>.Instance);

    private static WorkflowExecutable Executable(string artifactId, ExecutableNode root) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", "sha256:v1"),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UnixEpoch,
            compatibilityMetadata: new Dictionary<string, string>());

    private static ExecutableNode TriggerNode(string nodeId, string activityType) =>
        Node(nodeId, activityType, new Dictionary<string, string> { [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType });

    private static ExecutableNode PlainNode(string nodeId, string activityType) =>
        Node(nodeId, activityType, new Dictionary<string, string>());

    private static ExecutableNode Node(string nodeId, string activityType, Dictionary<string, string> metadata)
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
            metadata: metadata);
    }

    private sealed class FakeInner : IWorkflowTriggerIndexer
    {
        public bool Called { get; private set; }

        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
        {
            Called = true;
            return new ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>>(Array.Empty<WorkflowTriggerBinding>());
        }
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
