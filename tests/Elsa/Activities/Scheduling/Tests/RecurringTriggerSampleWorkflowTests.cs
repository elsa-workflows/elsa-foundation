using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Scheduling;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Timer = Elsa.Activities.Scheduling.Activities.Timer;

namespace Elsa.Activities.Scheduling.Tests;

/// <summary>
/// Sample-workflow coverage for the Timer/Cron recurring start triggers (W16). It wires the real publish-time
/// providers, schedule indexer, in-memory schedule store, and the recurring-trigger pump end to end: publishing
/// a workflow whose start trigger is a <see cref="Timer"/> (or <see cref="Cron"/>) writes a recurring schedule,
/// and a pump sweep after the first occurrence dispatches a start-only stimulus through the router — the exact
/// path a host runs, minus the provider-neutral persistence swap.
/// </summary>
public sealed class RecurringTriggerSampleWorkflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private const string SlotId = "definition-1:default";

    [Fact]
    public async Task TimerTrigger_PublishThenPump_StartsInstanceOnFirstOccurrence()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        var calculator = new RecurringScheduleCalculator();
        var clock = new FixedClock(Now);
        var indexer = new RecurringTriggerScheduleIndexer(
            new NoopInner(),
            [new TimerRecurringScheduleProvider()],
            store, calculator, clock, NullLogger<RecurringTriggerScheduleIndexer>.Instance);

        // Activate a workflow that starts on a 5-minute timer.
        await PrepareAndActivateAsync(indexer, store, Workflow("artifact-timer", TimerNode("PT5M")), "activation-1");

        // Before the first occurrence nothing is due.
        var bindingStore = await BindingStoreAsync("artifact-timer", TimerStimulus.StimulusType, TimerStimulus.Hash("PT5M"), "activation-1");
        var router = new RecordingRouter();
        var pump = Pump(store, bindingStore, router, calculator, new FixedClock(Now.AddMinutes(1)));
        await pump.ExecuteAsync(CancellationToken.None);
        Assert.Empty(router.Requests);

        // After the first occurrence a single start-only stimulus fires with the Timer identity.
        pump = Pump(store, bindingStore, router, calculator, new FixedClock(Now.AddMinutes(6)));
        await pump.ExecuteAsync(CancellationToken.None);

        var request = Assert.Single(router.Requests);
        Assert.Equal(TimerStimulus.StimulusType, request.StimulusType);
        Assert.Equal(TimerStimulus.Hash("PT5M"), request.StimulusHash);
        Assert.Equal(StimulusRoutingMode.StartOnly, request.Mode);
    }

    [Fact]
    public async Task CronTrigger_PublishThenPump_StartsInstanceOnFirstOccurrence()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        var calculator = new RecurringScheduleCalculator();
        var indexer = new RecurringTriggerScheduleIndexer(
            new NoopInner(),
            [new CronRecurringScheduleProvider()],
            store, calculator, new FixedClock(Now), NullLogger<RecurringTriggerScheduleIndexer>.Instance);

        // Every hour on the hour; activated at 12:00 the first occurrence is 13:00.
        await PrepareAndActivateAsync(indexer, store, Workflow("artifact-cron", CronNode("0 * * * *")), "activation-1");

        var bindingStore = await BindingStoreAsync("artifact-cron", CronStimulus.StimulusType, CronStimulus.Hash("0 * * * *"), "activation-1");
        var router = new RecordingRouter();
        var pump = Pump(store, bindingStore, router, calculator, new FixedClock(Now.AddHours(1).AddMinutes(1)));
        await pump.ExecuteAsync(CancellationToken.None);

        var request = Assert.Single(router.Requests);
        Assert.Equal(CronStimulus.StimulusType, request.StimulusType);
        Assert.Equal(CronStimulus.Hash("0 * * * *"), request.StimulusHash);
        Assert.Equal(StimulusRoutingMode.StartOnly, request.Mode);
    }

    [Fact]
    public async Task Reactivation_ReplacesTheServingSchedule_ForSameArtifact()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        var calculator = new RecurringScheduleCalculator();
        var indexer = new RecurringTriggerScheduleIndexer(
            new NoopInner(),
            [new TimerRecurringScheduleProvider()],
            store, calculator, new FixedClock(Now), NullLogger<RecurringTriggerScheduleIndexer>.Instance);

        await PrepareAndActivateAsync(indexer, store, Workflow("artifact-timer", TimerNode("PT5M")), "activation-1");
        await PrepareAndActivateAsync(
            indexer, store, Workflow("artifact-timer", TimerNode("PT9M")), "activation-2", replacedActivationId: "activation-1");

        // Only the re-activated schedule serves; supersession is activation-scoped, never an artifact-wide wipe.
        var schedule = Assert.Single(await store.ListDueAsync(Now.AddHours(1), 10));
        Assert.Equal(TimerStimulus.Hash("PT9M"), schedule.StimulusHash);
        Assert.Equal("activation-2", schedule.ActivationId);
    }

    private static RecurringTriggerPumpTask Pump(
        IRecurringTriggerScheduleStore store,
        IWorkflowTriggerBindingStore bindingStore,
        RecordingRouter router,
        IRecurringScheduleCalculator calculator,
        TimeProvider clock) =>
        new(store, bindingStore, router, calculator,
            Microsoft.Extensions.Options.Options.Create(new RecurringTriggerPumpOptions()),
            clock, NullLogger<RecurringTriggerPumpTask>.Instance);

    /// <summary>
    /// Prepares the activation's recurring projection and then makes it serve — the two halves the activation
    /// coordinator performs around the slot CAS. Preparation alone leaves the schedule invisible to the pump.
    /// </summary>
    private static async Task PrepareAndActivateAsync(
        RecurringTriggerScheduleIndexer indexer,
        IRecurringTriggerScheduleStore store,
        WorkflowExecutable executable,
        string activationId,
        string? replacedActivationId = null)
    {
        await indexer.PrepareActivationAsync(executable, activationId, SlotId);
        await store.ActivateAsync(activationId, replacedActivationId);
    }

    // The trigger binding the activation would have prepared for the same node — the pump only dispatches a fire
    // through the binding its schedule owns, matching on artifact, node, activation and slot.
    private static async Task<InMemoryWorkflowTriggerBindingStore> BindingStoreAsync(
        string artifactId,
        string stimulusType,
        string stimulusHash,
        string activationId)
    {
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(new WorkflowTriggerBinding(
            TriggerBindingId: WorkflowTriggerBinding.BuildId(activationId, artifactId, "node-trigger", stimulusHash),
            ArtifactId: artifactId,
            DefinitionId: "definition-1",
            ArtifactVersion: "1.0.0",
            ArtifactHash: "sha256:v1",
            ExecutableNodeId: "node-trigger",
            StimulusType: stimulusType,
            StimulusHash: stimulusHash,
            CorrelationScope: null,
            Metadata: new Dictionary<string, string>(),
            CreatedAt: Now,
            ActivationId: activationId,
            SlotId: SlotId));
        return bindingStore;
    }

    private static WorkflowExecutable Workflow(string artifactId, ExecutableNode trigger) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", "sha256:v1"),
            rootActivity: trigger,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: Now,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    private static ExecutableNode TimerNode(string interval) =>
        TriggerNode(Timer.ActivityType, nameof(Timer.Interval), interval);

    private static ExecutableNode CronNode(string expression) =>
        TriggerNode(Cron.ActivityType, nameof(Cron.Expression), expression);

    private static ExecutableNode TriggerNode(string activityType, string inputName, string literal)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        using var value = JsonDocument.Parse(JsonSerializer.Serialize(literal));
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [inputName] = LiteralBinding(inputName, value.RootElement)
        };

        return new ExecutableNode(
            executableNodeId: "node-trigger",
            authoredActivityId: "authored-node-trigger",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, document.RootElement.Clone()),
            inputBindings: bindings,
            metadata: new Dictionary<string, string> { [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType });
    }

    private static RuntimeInputBinding LiteralBinding(string name, JsonElement value)
    {
        var type = new ValueTypeDescriptor("String");
        return new RuntimeInputBinding(
            name,
            type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, value, ValueProtectionPolicy.InstanceInline));
    }

    private sealed class NoopInner : IWorkflowTriggerIndexer
    {
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(
            WorkflowExecutable executable,
            string activationId,
            string slotId,
            CancellationToken cancellationToken = default) =>
            new(Array.Empty<WorkflowTriggerBinding>());
    }

    private sealed class RecordingRouter : IStimulusRouter
    {
        public List<StimulusDispatchRequest> Requests { get; } = new();

        public ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return new ValueTask<StimulusRoutingResult>(new StimulusRoutingResult([], []));
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
