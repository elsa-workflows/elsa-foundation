using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
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
    public async Task PublicationScopedSchedulesReplaceAndRemoveOneAuthorityWithoutTouchingAnother()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        var indexer = CreateIndexer(
            new FakeInner(),
            store,
            new FakeScheduleProvider("Elsa.Timer", "Timer", "timer-shared", "PT5M"));
        var executable = Executable("artifact-shared", TriggerNode("node-timer", "Elsa.Timer"));

        await indexer.PrepareActivationAsync(executable, "publication-default-v1", "slot-default");
        await indexer.PrepareActivationAsync(executable, "publication-blue", "slot-blue");

        Assert.Empty(await store.ListDueAsync(Now.AddMinutes(10), 10));
        var defaultSchedule = Assert.Single(await store.ListByActivationAsync("publication-default-v1"));
        var blueSchedule = Assert.Single(await store.ListByActivationAsync("publication-blue"));
        Assert.Equal("publication-default-v1", defaultSchedule.PublicationId);
        Assert.Equal("slot-default", defaultSchedule.SlotId);
        Assert.Equal("publication-blue", blueSchedule.PublicationId);
        Assert.Equal("slot-blue", blueSchedule.SlotId);
        Assert.NotEqual(defaultSchedule.ScheduleId, blueSchedule.ScheduleId);

        await store.ActivateAsync("publication-default-v1", replacedPublicationId: null);
        await store.ActivateAsync("publication-blue", replacedPublicationId: null);

        Assert.Equal(
            ["publication-blue", "publication-default-v1"],
            (await store.ListDueAsync(Now.AddMinutes(10), 10))
                .Select(schedule => schedule.PublicationId)
                .Order(StringComparer.Ordinal));

        await indexer.PrepareActivationAsync(executable, "publication-default-v2", "slot-default");
        await store.ActivateAsync("publication-default-v2", replacedPublicationId: "publication-default-v1");

        Assert.Equal(
            ["publication-blue", "publication-default-v2"],
            (await store.ListDueAsync(Now.AddMinutes(10), 10))
                .Select(schedule => schedule.PublicationId)
                .Order(StringComparer.Ordinal));

        await store.DeleteByActivationAsync("publication-default-v1");
        await store.DeleteByActivationAsync("publication-default-v2");

        var survivingSchedule = Assert.Single(await store.ListDueAsync(Now.AddMinutes(10), 10));
        Assert.Equal("publication-blue", survivingSchedule.PublicationId);
        Assert.Equal("slot-blue", survivingSchedule.SlotId);
    }

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
            RecurringTriggerSchedule.BuildId("artifact-1", "old-node"), "artifact-1", "old-node", "Timer", "old", RecurringScheduleKind.Interval, "PT9M", Now, Now));
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
            new FakeScheduleProvider("Elsa.Cron", "Cron", "hash-cron", "0 0 30 2 *", RecurringScheduleKind.Cron, "test.cron"));

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(async () =>
            await indexer.IndexAsync(Executable("artifact-1", TriggerNode("node-1", "Elsa.Cron"))));

        Assert.Equal("artifact-1", exception.ArtifactId);
        Assert.Equal("node-1", exception.ExecutableNodeId);
        Assert.Equal(["test.cron"], exception.ProviderIds);
        Assert.Contains("0 0 30 2 *", exception.Message, StringComparison.Ordinal);
        Assert.False(inner.Called);
        Assert.Equal([priorBinding], await bindingStore.ListAllByArtifactAsync("artifact-1"));
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
            new FakeScheduleProvider("Elsa.Cron", "Cron", "cron-hash", "not-a-cron", RecurringScheduleKind.Cron, "test.cron"));
        var root = PlainNode("root", "Elsa.Sequence",
            TriggerNode("invalid-later", "Elsa.Cron"),
            TriggerNode("valid-first", "Elsa.Timer"));

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(async () =>
            await indexer.IndexAsync(Executable("artifact-1", root)));

        Assert.Equal(["test.cron"], exception.ProviderIds);
        Assert.Contains("not-a-cron", exception.Message, StringComparison.Ordinal);
        Assert.IsType<FormatException>(exception.InnerException);
        Assert.False(inner.Called);
        Assert.Equal(priorSchedule, await store.FindAsync(priorSchedule.ScheduleId));
        Assert.Null(await store.FindAsync(RecurringTriggerSchedule.BuildId("artifact-1", "valid-first")));
    }

    [Fact]
    public async Task Index_DescriptorFailure_CarriesProviderAndPreservesExpressionContext()
    {
        const string expression = "descriptor-expression";
        var expected = new ArgumentException($"Recurring expression '{expression}' is invalid.");
        var provider = new ThrowingScheduleProvider("Elsa.Cron", "test.cron", expected);
        var indexer = CreateIndexer(new FakeInner(), new InMemoryRecurringTriggerScheduleStore(), provider);

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(async () =>
            await indexer.IndexAsync(Executable("artifact-1", TriggerNode("node-1", "Elsa.Cron"))));

        Assert.Equal(["test.cron"], exception.ProviderIds);
        Assert.Same(expected, exception.InnerException);
        Assert.Contains(expression, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Index_CalculatorFailure_CarriesProviderAndExpressionAndPreservesInnerException()
    {
        const string expression = "calculator-expression";
        var expected = new InvalidOperationException("calculator failed");
        var provider = new FakeScheduleProvider("Elsa.Cron", "Cron", "cron-hash", expression, RecurringScheduleKind.Cron, "test.cron");
        var indexer = CreateIndexer(
            new FakeInner(),
            new InMemoryRecurringTriggerScheduleStore(),
            new ThrowingCalculator(expected),
            provider);

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(async () =>
            await indexer.IndexAsync(Executable("artifact-1", TriggerNode("node-1", "Elsa.Cron"))));

        Assert.Equal(["test.cron"], exception.ProviderIds);
        Assert.Contains(expression, exception.Message, StringComparison.Ordinal);
        Assert.Same(expected, exception.InnerException);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Index_ClaimingProviderWithInvalidIdentity_FailsBeforeScheduleCalculationOrMutation(string? providerId)
    {
        var events = new List<string>();
        var inner = new FakeInner(() => events.Add("inner"));
        var store = new RecordingScheduleStore(events);
        var calculator = new RecordingCalculator(events);
        var provider = new FakeScheduleProvider("Elsa.Timer", "Timer", "timer-hash", "PT5M", providerId: providerId);
        var indexer = CreateIndexer(inner, store, calculator, provider);

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(async () =>
            await indexer.IndexAsync(Executable("artifact-1", TriggerNode("node-1", "Elsa.Timer"))));

        Assert.Equal("artifact-1", exception.ArtifactId);
        Assert.Equal("node-1", exception.ExecutableNodeId);
        Assert.Equal("Elsa.Timer", exception.ActivityType);
        Assert.Equal("ProviderIdentity", exception.Facet);
        Assert.Empty(exception.ProviderIds);
        Assert.Contains("provider id", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(events);
        Assert.False(inner.Called);
    }

    [Fact]
    public async Task Index_MultipleProvidersRecognizeSameNode_FailsBeforeScheduleCalculationOrMutation()
    {
        var events = new List<string>();
        var inner = new FakeInner(() => events.Add("inner"));
        var store = new RecordingScheduleStore(events);
        var calculator = new RecordingCalculator(events);
        var indexer = CreateIndexer(
            inner,
            store,
            calculator,
            new FakeScheduleProvider("Elsa.Timer", "Timer", "timer-hash-1", "PT5M", providerId: "test.timer.1"),
            new FakeScheduleProvider("Elsa.Timer", "Timer", "timer-hash-2", "PT10M", providerId: "test.timer.2"));

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(async () =>
            await indexer.IndexAsync(Executable("artifact-1", TriggerNode("node-1", "Elsa.Timer"))));

        Assert.Equal("ProviderRecognition", exception.Facet);
        Assert.Equal(["test.timer.1", "test.timer.2"], exception.ProviderIds);
        Assert.Empty(events);
        Assert.False(inner.Called);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Index_ThrowingProviderWithInvalidIdentity_FailsSafelyBeforeMutation(string? providerId)
    {
        var expected = new InvalidOperationException("descriptor failed");
        var events = new List<string>();
        var inner = new FakeInner(() => events.Add("inner"));
        var store = new RecordingScheduleStore(events);
        var provider = new ThrowingScheduleProvider("Elsa.Cron", providerId, expected);
        var indexer = CreateIndexer(inner, store, provider);

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(async () =>
            await indexer.IndexAsync(Executable("artifact-1", TriggerNode("node-1", "Elsa.Cron"))));

        Assert.Equal("artifact-1", exception.ArtifactId);
        Assert.Equal("node-1", exception.ExecutableNodeId);
        Assert.Equal("Elsa.Cron", exception.ActivityType);
        Assert.Equal("ProviderIdentity", exception.Facet);
        Assert.Empty(exception.ProviderIds);
        Assert.Contains("provider id", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(expected, exception.InnerException);
        Assert.Empty(events);
        Assert.False(inner.Called);
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
        new(RecurringTriggerSchedule.BuildId(artifactId, nodeId), artifactId, nodeId, "Cron", stimulusHash,
            RecurringScheduleKind.Cron, "0 * * * *", Now.AddHours(1), Now);

    private static WorkflowExecutable Executable(string artifactId, ExecutableNode root) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", "sha256:v1"),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UnixEpoch,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

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
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, document.RootElement.Clone()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
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

    private sealed class ThrowingCalculator(Exception exception) : IRecurringScheduleCalculator
    {
        public DateTimeOffset? ComputeNext(RecurringScheduleKind kind, string expression, DateTimeOffset after) =>
            throw exception;
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
        RecurringScheduleKind kind = RecurringScheduleKind.Interval,
        string? providerId = "test.recurring") : IRecurringTriggerScheduleProvider
    {
        public string ProviderId => providerId!;

        public IReadOnlyCollection<RecurringScheduleDescriptor> Describe(ExecutableNode node) =>
            StringComparer.Ordinal.Equals(node.ActivityType, activityType)
                ? [new RecurringScheduleDescriptor(stimulusType, stimulusHash, kind, expression)]
                : [];
    }

    private sealed class ThrowingScheduleProvider(string activityType, string? providerId, Exception exception) : IRecurringTriggerScheduleProvider
    {
        public string ProviderId => providerId!;

        public IReadOnlyCollection<RecurringScheduleDescriptor> Describe(ExecutableNode node)
        {
            if (!StringComparer.Ordinal.Equals(node.ActivityType, activityType))
                return [];

            throw exception;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
