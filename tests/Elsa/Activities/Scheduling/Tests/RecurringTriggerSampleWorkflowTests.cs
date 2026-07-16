using System.Text.Json;
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

        // Publish a workflow that starts on a 5-minute timer.
        await indexer.IndexAsync(Workflow("artifact-timer", TimerNode("PT5M")));

        // Before the first occurrence nothing is due.
        var router = new RecordingRouter();
        var pump = Pump(store, router, calculator, new FixedClock(Now.AddMinutes(1)));
        await pump.ExecuteAsync(CancellationToken.None);
        Assert.Empty(router.Requests);

        // After the first occurrence a single start-only stimulus fires with the Timer identity.
        pump = Pump(store, router, calculator, new FixedClock(Now.AddMinutes(6)));
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

        // Every hour on the hour; published at 12:00 the first occurrence is 13:00.
        await indexer.IndexAsync(Workflow("artifact-cron", CronNode("0 * * * *")));

        var router = new RecordingRouter();
        var pump = Pump(store, router, calculator, new FixedClock(Now.AddHours(1).AddMinutes(1)));
        await pump.ExecuteAsync(CancellationToken.None);

        var request = Assert.Single(router.Requests);
        Assert.Equal(CronStimulus.StimulusType, request.StimulusType);
        Assert.Equal(CronStimulus.Hash("0 * * * *"), request.StimulusHash);
        Assert.Equal(StimulusRoutingMode.StartOnly, request.Mode);
    }

    [Fact]
    public async Task Republish_ReplacesSchedule_ForSameArtifact()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        var calculator = new RecurringScheduleCalculator();
        var indexer = new RecurringTriggerScheduleIndexer(
            new NoopInner(),
            [new TimerRecurringScheduleProvider()],
            store, calculator, new FixedClock(Now), NullLogger<RecurringTriggerScheduleIndexer>.Instance);

        await indexer.IndexAsync(Workflow("artifact-timer", TimerNode("PT5M")));
        await indexer.IndexAsync(Workflow("artifact-timer", TimerNode("PT9M")));

        // Only the republished schedule survives, keyed by the same node id.
        var schedule = Assert.Single(await store.ListDueAsync(Now.AddHours(1), 10));
        Assert.Equal(TimerStimulus.Hash("PT9M"), schedule.StimulusHash);
    }

    private static RecurringTriggerPumpTask Pump(
        IRecurringTriggerScheduleStore store,
        RecordingRouter router,
        IRecurringScheduleCalculator calculator,
        TimeProvider clock) =>
        new(store, router, calculator,
            Microsoft.Extensions.Options.Options.Create(new RecurringTriggerPumpOptions()),
            clock, NullLogger<RecurringTriggerPumpTask>.Instance);

    private static WorkflowExecutable Workflow(string artifactId, ExecutableNode trigger) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", "sha256:v1"),
            rootActivity: trigger,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: Now,
            compatibilityMetadata: new Dictionary<string, string>());

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
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: bindings,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
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
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) =>
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
