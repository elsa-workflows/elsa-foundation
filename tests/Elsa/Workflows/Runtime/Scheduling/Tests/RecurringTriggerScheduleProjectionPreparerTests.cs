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

public sealed class RecurringTriggerScheduleProjectionPreparerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PublicationScopedSchedulesReplaceAndRemoveOneAuthorityWithoutTouchingAnother()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        var preparer = CreatePreparer(
            store,
            new FakeScheduleProvider("Elsa.Timer", "Timer", "timer-shared", "PT5M"));
        var executable = Executable("artifact-shared", TriggerNode("node-timer", "Elsa.Timer"));

        await preparer.PrepareActivationAsync(executable, "publication-default-v1", "slot-default");
        await preparer.PrepareActivationAsync(executable, "publication-blue", "slot-blue");

        Assert.Empty(await store.ListDueAsync(Now.AddMinutes(10), 10));
        var defaultSchedule = Assert.Single(await store.ListByActivationAsync("publication-default-v1"));
        var blueSchedule = Assert.Single(await store.ListByActivationAsync("publication-blue"));
        Assert.Equal("publication-default-v1", defaultSchedule.ActivationId);
        Assert.Equal("slot-default", defaultSchedule.SlotId);
        Assert.Equal("publication-blue", blueSchedule.ActivationId);
        Assert.Equal("slot-blue", blueSchedule.SlotId);
        Assert.NotEqual(defaultSchedule.ScheduleId, blueSchedule.ScheduleId);

        await store.ActivateAsync("publication-default-v1", replacedActivationId: null);
        await store.ActivateAsync("publication-blue", replacedActivationId: null);

        Assert.Equal(
            ["publication-blue", "publication-default-v1"],
            (await store.ListDueAsync(Now.AddMinutes(10), 10))
                .Select(schedule => schedule.ActivationId)
                .Order(StringComparer.Ordinal));

        await preparer.PrepareActivationAsync(executable, "publication-default-v2", "slot-default");
        await store.ActivateAsync("publication-default-v2", replacedActivationId: "publication-default-v1");

        Assert.Equal(
            ["publication-blue", "publication-default-v2"],
            (await store.ListDueAsync(Now.AddMinutes(10), 10))
                .Select(schedule => schedule.ActivationId)
                .Order(StringComparer.Ordinal));

        await store.DeleteByActivationAsync("publication-default-v1");
        await store.DeleteByActivationAsync("publication-default-v2");

        var survivingSchedule = Assert.Single(await store.ListDueAsync(Now.AddMinutes(10), 10));
        Assert.Equal("publication-blue", survivingSchedule.ActivationId);
        Assert.Equal("slot-blue", survivingSchedule.SlotId);
    }

    [Fact]
    public void The_preparer_no_longer_carries_an_artifact_scoped_write_path()
    {
        // T041/T045: the removed IndexAsync path deleted every schedule of the artifact and wrote rows born
        // active, bypassing prepare/activate. The contract half of this assertion lives in
        // Elsa.Workflows.Runtime.Tests' WorkflowTriggerIndexerContractTests.
        Assert.Null(typeof(RecurringTriggerScheduleProjectionPreparer).GetMethod("IndexAsync"));
    }

    [Fact]
    public void The_preparer_is_not_a_trigger_indexer()
    {
        // T044b. It used to be one — a decorator wearing IWorkflowTriggerIndexer's contract, which made that
        // replacement contract silently own this projection as well. Replacing the indexer must no longer be
        // able to disarm the recurring projection, and the type system now says so.
        Assert.False(typeof(IWorkflowTriggerIndexer).IsAssignableFrom(typeof(RecurringTriggerScheduleProjectionPreparer)));
    }

    [Fact]
    public async Task PrepareActivation_MaterializesEverySchedule_BeforeTheProjectionWrite()
    {
        var events = new List<string>();
        var store = new RecordingScheduleStore(events);
        var calculator = new RecordingCalculator(events);
        var preparer = CreatePreparer(
            store,
            calculator,
            new FakeScheduleProvider("Elsa.Timer", "Timer", "hash-1", "PT5M"),
            new FakeScheduleProvider("Elsa.Cron", "Cron", "hash-2", "0 * * * *", RecurringScheduleKind.Cron));

        var root = PlainNode("root", "Elsa.Sequence",
            TriggerNode("timer", "Elsa.Timer"),
            TriggerNode("cron", "Elsa.Cron"));
        await preparer.PrepareActivationAsync(Executable("artifact-1", root), "activation-1", "slot-default");

        // ONE owned write for the whole recurring projection, and no artifact-wide delete before it: the removed
        // IndexAsync path emitted "delete" + a save per row instead (FR-B-006 writer census, findings 1 and 3).
        Assert.Equal(["calculate:Cron", "calculate:Interval", "prepare:activation-1:2"], events);
    }

    [Fact]
    public async Task PrepareActivation_IgnoresNonTriggerNodes()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        var preparer = CreatePreparer(store, new FakeScheduleProvider("Elsa.Timer", "Timer", "hash-1", "PT5M"));

        await preparer.PrepareActivationAsync(
            Executable("artifact-1", PlainNode("node-1", "Elsa.Timer")),
            "activation-1",
            "slot-default");

        Assert.Empty(await store.ListByActivationAsync("activation-1"));
    }

    [Fact]
    public async Task PrepareActivation_LeavesSchedulesOfAnotherActivationOnTheSameArtifactIntact()
    {
        // The removed IndexAsync path deleted every schedule of the artifact before writing. Preparation is
        // activation-scoped: a schedule owned by another activation of the same artifact is untouched.
        var store = new InMemoryRecurringTriggerScheduleStore();
        await store.SaveAsync(new RecurringTriggerSchedule(
            RecurringTriggerSchedule.BuildId("artifact-1", "old-node"), "artifact-1", "old-node", "Timer", "old", RecurringScheduleKind.Interval, "PT9M", Now, Now));
        var preparer = CreatePreparer(store, new FakeScheduleProvider("Elsa.Timer", "Timer", "hash-1", "PT5M"));

        await preparer.PrepareActivationAsync(
            Executable("artifact-1", TriggerNode("node-1", "Elsa.Timer")),
            "activation-1",
            "slot-default");

        // The foreign row still serves; the prepared one does not yet.
        var serving = Assert.Single(await store.ListDueAsync(Now.AddMinutes(10), 10));
        Assert.Equal(RecurringTriggerSchedule.BuildId("artifact-1", "old-node"), serving.ScheduleId);
        var prepared = Assert.Single(await store.ListByActivationAsync("activation-1"));
        Assert.Equal(RecurringTriggerSchedule.BuildId("activation-1", "artifact-1", "node-1"), prepared.ScheduleId);
        Assert.False(prepared.IsActive);
        Assert.Equal("slot-default", prepared.SlotId);
    }

    [Fact]
    public async Task PrepareActivation_ExhaustedCron_FailsBeforeMutationAndPreservesSeededSchedules()
    {
        var scheduleStore = new InMemoryRecurringTriggerScheduleStore();
        var priorSchedule = Schedule("artifact-1", "old-node", "old-hash");
        await scheduleStore.SaveAsync(priorSchedule);
        var preparer = CreatePreparer(scheduleStore,
            new FakeScheduleProvider("Elsa.Cron", "Cron", "hash-cron", "0 0 30 2 *", RecurringScheduleKind.Cron, "test.cron"));

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(async () =>
            await preparer.PrepareActivationAsync(
                Executable("artifact-1", TriggerNode("node-1", "Elsa.Cron")),
                "activation-1",
                "slot-default"));

        Assert.Equal("artifact-1", exception.ArtifactId);
        Assert.Equal("node-1", exception.ExecutableNodeId);
        Assert.Equal(["test.cron"], exception.ProviderIds);
        Assert.Contains("0 0 30 2 *", exception.Message, StringComparison.Ordinal);
        Assert.Equal(priorSchedule, await scheduleStore.FindAsync(priorSchedule.ScheduleId));
    }

    [Fact]
    public async Task PrepareActivation_InvalidLaterNode_FailsBeforeMutationAndPreservesPriorSchedule()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        var priorSchedule = Schedule("artifact-1", "old-node", "old-hash");
        await store.SaveAsync(priorSchedule);
        var preparer = CreatePreparer(store,
            new FakeScheduleProvider("Elsa.Timer", "Timer", "timer-hash", "PT5M"),
            new FakeScheduleProvider("Elsa.Cron", "Cron", "cron-hash", "not-a-cron", RecurringScheduleKind.Cron, "test.cron"));
        var root = PlainNode("root", "Elsa.Sequence",
            TriggerNode("invalid-later", "Elsa.Cron"),
            TriggerNode("valid-first", "Elsa.Timer"));

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(async () =>
            await preparer.PrepareActivationAsync(Executable("artifact-1", root), "activation-1", "slot-default"));

        Assert.Equal(["test.cron"], exception.ProviderIds);
        Assert.Contains("not-a-cron", exception.Message, StringComparison.Ordinal);
        Assert.IsType<FormatException>(exception.InnerException);
        Assert.Equal(priorSchedule, await store.FindAsync(priorSchedule.ScheduleId));
        Assert.Null(await store.FindAsync(RecurringTriggerSchedule.BuildId("activation-1", "artifact-1", "valid-first")));
    }

    [Fact]
    public async Task PrepareActivation_DescriptorFailure_CarriesProviderAndPreservesExpressionContext()
    {
        const string expression = "descriptor-expression";
        var expected = new ArgumentException($"Recurring expression '{expression}' is invalid.");
        var provider = new ThrowingScheduleProvider("Elsa.Cron", "test.cron", expected);
        var preparer = CreatePreparer(new InMemoryRecurringTriggerScheduleStore(), provider);

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(async () =>
            await preparer.PrepareActivationAsync(
                Executable("artifact-1", TriggerNode("node-1", "Elsa.Cron")),
                "activation-1",
                "slot-default"));

        Assert.Equal(["test.cron"], exception.ProviderIds);
        Assert.Same(expected, exception.InnerException);
        Assert.Contains(expression, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareActivation_CalculatorFailure_CarriesProviderAndExpressionAndPreservesInnerException()
    {
        const string expression = "calculator-expression";
        var expected = new InvalidOperationException("calculator failed");
        var provider = new FakeScheduleProvider("Elsa.Cron", "Cron", "cron-hash", expression, RecurringScheduleKind.Cron, "test.cron");
        var preparer = CreatePreparer(
            new InMemoryRecurringTriggerScheduleStore(),
            new ThrowingCalculator(expected),
            provider);

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(async () =>
            await preparer.PrepareActivationAsync(
                Executable("artifact-1", TriggerNode("node-1", "Elsa.Cron")),
                "activation-1",
                "slot-default"));

        Assert.Equal(["test.cron"], exception.ProviderIds);
        Assert.Contains(expression, exception.Message, StringComparison.Ordinal);
        Assert.Same(expected, exception.InnerException);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task PrepareActivation_ClaimingProviderWithInvalidIdentity_FailsBeforeScheduleCalculationOrMutation(string? providerId)
    {
        var events = new List<string>();
        var store = new RecordingScheduleStore(events);
        var calculator = new RecordingCalculator(events);
        var provider = new FakeScheduleProvider("Elsa.Timer", "Timer", "timer-hash", "PT5M", providerId: providerId);
        var preparer = CreatePreparer(store, calculator, provider);

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(async () =>
            await preparer.PrepareActivationAsync(
                Executable("artifact-1", TriggerNode("node-1", "Elsa.Timer")),
                "activation-1",
                "slot-default"));

        Assert.Equal("artifact-1", exception.ArtifactId);
        Assert.Equal("node-1", exception.ExecutableNodeId);
        Assert.Equal("Elsa.Timer", exception.ActivityType);
        Assert.Equal("ProviderIdentity", exception.Facet);
        Assert.Empty(exception.ProviderIds);
        Assert.Contains("provider id", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(events);
    }

    [Fact]
    public async Task PrepareActivation_MultipleProvidersRecognizeSameNode_FailsBeforeScheduleCalculationOrMutation()
    {
        var events = new List<string>();
        var store = new RecordingScheduleStore(events);
        var calculator = new RecordingCalculator(events);
        var preparer = CreatePreparer(
            store,
            calculator,
            new FakeScheduleProvider("Elsa.Timer", "Timer", "timer-hash-1", "PT5M", providerId: "test.timer.1"),
            new FakeScheduleProvider("Elsa.Timer", "Timer", "timer-hash-2", "PT10M", providerId: "test.timer.2"));

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(async () =>
            await preparer.PrepareActivationAsync(
                Executable("artifact-1", TriggerNode("node-1", "Elsa.Timer")),
                "activation-1",
                "slot-default"));

        Assert.Equal("ProviderRecognition", exception.Facet);
        Assert.Equal(["test.timer.1", "test.timer.2"], exception.ProviderIds);
        Assert.Empty(events);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task PrepareActivation_ThrowingProviderWithInvalidIdentity_FailsSafelyBeforeMutation(string? providerId)
    {
        var expected = new InvalidOperationException("descriptor failed");
        var events = new List<string>();
        var store = new RecordingScheduleStore(events);
        var provider = new ThrowingScheduleProvider("Elsa.Cron", providerId, expected);
        var preparer = CreatePreparer(store, provider);

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(async () =>
            await preparer.PrepareActivationAsync(
                Executable("artifact-1", TriggerNode("node-1", "Elsa.Cron")),
                "activation-1",
                "slot-default"));

        Assert.Equal("artifact-1", exception.ArtifactId);
        Assert.Equal("node-1", exception.ExecutableNodeId);
        Assert.Equal("Elsa.Cron", exception.ActivityType);
        Assert.Equal("ProviderIdentity", exception.Facet);
        Assert.Empty(exception.ProviderIds);
        Assert.Contains("provider id", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(expected, exception.InnerException);
        Assert.Empty(events);
    }

    [Fact]
    public async Task PrepareActivation_WithNoProviders_StillPreparesAnExplicitEmptyProjection()
    {
        // The preparer is the SINGLE owner of the recurring projection's preparation (FR-B-006 writer census,
        // finding 3). It therefore prepares even with nothing to materialize, so no caller has to read the
        // projection back and re-prepare it just to make "no schedules" explicit.
        var events = new List<string>();
        var store = new RecordingScheduleStore(events);
        var preparer = new RecurringTriggerScheduleProjectionPreparer(
            [], store, new RecurringScheduleCalculator(), new FixedClock(Now),
            NullLogger<RecurringTriggerScheduleProjectionPreparer>.Instance);

        await preparer.PrepareActivationAsync(
            Executable("artifact-1", TriggerNode("node-1", "Elsa.Timer")),
            "activation-1",
            "slot-default");

        Assert.Equal(["prepare:activation-1:0"], events);
        Assert.Empty(await store.ListDueAsync(Now.AddMinutes(10), 10));
    }

    private static RecurringTriggerScheduleProjectionPreparer CreatePreparer(
        IRecurringTriggerScheduleStore store,
        params IRecurringTriggerScheduleProvider[] providers) =>
        CreatePreparer(store, new RecurringScheduleCalculator(), providers);

    private static RecurringTriggerScheduleProjectionPreparer CreatePreparer(
        IRecurringTriggerScheduleStore store,
        IRecurringScheduleCalculator calculator,
        params IRecurringTriggerScheduleProvider[] providers) =>
        new(providers, store, calculator, new FixedClock(Now),
            NullLogger<RecurringTriggerScheduleProjectionPreparer>.Instance);

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

        public ValueTask PrepareActivationAsync(
            string activationId,
            IReadOnlyCollection<RecurringTriggerSchedule> schedules,
            CancellationToken cancellationToken = default)
        {
            // Records the activation AND the row count, so a test can prove there was exactly one write and that
            // it carried the whole materialized set.
            events.Add($"prepare:{activationId}:{schedules.Count}");
            return _inner.PrepareActivationAsync(activationId, schedules, cancellationToken);
        }

        public ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByActivationPageAsync(
            RecurringTriggerScheduleActivationPageQuery query,
            CancellationToken cancellationToken = default) =>
            _inner.ListByActivationPageAsync(query, cancellationToken);

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
