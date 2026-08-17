using Elsa.Activities.Scheduling;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Services;
using Elsa.Workflows.Runtime.Reconciliation.Tests;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using Timer = Elsa.Activities.Scheduling.Activities.Timer;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

/// <summary>
/// T071a — an <b>imported</b> timer artifact actually fires.
/// </summary>
/// <remarks>
/// <para>
/// This is the projection the 2026-08-14 re-review added on the explicit grounds that binding-store-only
/// activation would import timer/cron workflows that never fire. Everything around it was already covered, but
/// only in halves: <c>RecurringTriggerScheduleIndexerTests</c> proves the indexer writes a prepared row,
/// <c>RecurringTriggerPumpTaskTests</c> proves the pump fires a hand-seeded one, and
/// <c>RecurringTriggerSampleWorkflowTests</c> joins them through a recording router that never starts anything.
/// None of them starts from a mounted closure, and none of them observes a workflow run.
/// </para>
/// <para>
/// So this drives the whole chain with nothing stubbed below it: a content-addressed closure on disk → the real
/// JSON source and importer → the activation coordinator → both projections → the real
/// <see cref="RecurringTriggerPumpTask"/> on a controlled clock → the real <see cref="IStimulusRouter"/> → a
/// workflow execution that completes. Time is supplied by <see cref="FakeTimeProvider"/>; nothing sleeps.
/// </para>
/// </remarks>
public sealed class ImportedRecurringTriggerEndToEndTests : IDisposable
{
    private const string SourceId = "mounted-artifacts";
    private const string DefinitionId = "definition-nightly-reconcile";
    private const string TriggerNodeId = "node-timer";
    private const string Interval = "PT5M";

    private static readonly DateTimeOffset Origin = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Origin);

    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-recurring-import-e2e",
        Guid.NewGuid().ToString("N"));

    public ImportedRecurringTriggerEndToEndTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task An_imported_timer_artifact_projects_an_activation_scoped_schedule()
    {
        await using var harness = BuildHarness();
        var executable = MountTimerArtifact(harness, Interval, "nightly.json");

        var entry = Assert.Single((await ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Imported, entry.Outcome);

        // The recurring projection is the half a binding-store-only activation would silently omit.
        var schedule = Assert.Single(await ScheduleStore(harness).ListByActivationAsync(entry.ActivationId!));
        Assert.Equal(entry.ActivationId, schedule.ActivationId);
        Assert.Equal(WorkflowActivationSlotIdentity.Create(DefinitionId, WorkflowArtifactReconciler.DefaultSlotName), schedule.SlotId);
        Assert.Equal(executable.Identity.ArtifactId, schedule.ArtifactId);
        Assert.Equal(TriggerNodeId, schedule.ExecutableNodeId);
        Assert.Equal(TimerStimulus.StimulusType, schedule.StimulusType);
        Assert.Equal(TimerStimulus.Hash(Interval), schedule.StimulusHash);
        Assert.Equal(RecurringScheduleKind.Interval, schedule.Kind);

        // Serving, not merely prepared: the coordinator flipped it after the slot CAS. A prepared row is
        // invisible to the pump, which is exactly how "imports fine, never fires" would look.
        Assert.True(schedule.IsActive);
        Assert.Equal(Origin.AddMinutes(5), schedule.NextOccurrence);

        // And its owning binding shares the activation scope the pump matches on.
        var binding = Assert.Single(await BindingStore(harness).ListAllByStimulusAsync(TimerStimulus.StimulusType, TimerStimulus.Hash(Interval)));
        Assert.Equal(schedule.ActivationId, binding.ActivationId);
        Assert.Equal(schedule.SlotId, binding.SlotId);
    }

    [Fact]
    public async Task The_pump_starts_the_imported_workflow_on_its_first_occurrence_and_it_runs_to_completion()
    {
        await using var harness = BuildHarness();
        var executable = MountTimerArtifact(harness, Interval, "nightly.json");
        var entry = Assert.Single((await ReconcileAsync(harness)).Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, entry.Outcome);

        // Before the first occurrence nothing is due — the schedule is real, not perpetually firing.
        _clock.Advance(TimeSpan.FromMinutes(1));
        await SweepAsync(harness);
        Assert.Null((await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId)).WorkflowState);

        // Past the first occurrence: the pump claims it, resolves the owning binding, and the router starts the
        // imported workflow for real.
        _clock.Advance(TimeSpan.FromMinutes(5));
        await SweepAsync(harness);

        var run = await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        run.AssertWorkflowCompleted();

        // It ran the artifact that was imported, on the reference the importer minted for this activation.
        var state = run.WorkflowState;
        Assert.NotNull(state);
        Assert.Equal(executable.Identity.ArtifactId, state!.PinnedExecutable.ArtifactId);
        Assert.Equal(WorkflowActivationReferenceIdentity.Create(entry.ActivationId!), state.PinnedSource!.SourceReferenceId);

        // Fired once, and the cursor moved to the first occurrence strictly after the wake instant (an interval
        // timer anchors on each fire, so 12:06 + PT5M), not to a replayed +PT5M from the stale cursor.
        var schedule = Assert.Single(await ScheduleStore(harness).ListByActivationAsync(entry.ActivationId!));
        Assert.Equal(Origin.AddMinutes(11), schedule.NextOccurrence);
    }

    [Fact]
    public async Task A_superseding_import_re_projects_the_schedule_instead_of_leaving_the_old_one_firing()
    {
        await using var harness = BuildHarness();
        MountTimerArtifact(harness, Interval, "nightly.json");
        var first = Assert.Single((await ReconcileAsync(harness)).Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, first.Outcome);

        // A new build of the same definition, mounted over the old one: same node, different authored interval,
        // therefore a different content-addressed artifact and a different stimulus identity.
        File.Delete(Path.Combine(_mount, "nightly.json"));
        var superseding = MountTimerArtifact(harness, "PT9M", "nightly-v2.json");
        var second = Assert.Single((await ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Imported, second.Outcome);
        Assert.NotEqual(first.ActivationId, second.ActivationId);

        // The predecessor's row is deactivated rather than deleted — supersession is activation-scoped, so the
        // coordinator retires the projection it owns and leaves it recoverable for compensation. What matters is
        // that it no longer serves: left firing it would run the retired artifact on its own cadence, giving one
        // definition two live recurrences.
        var store = ScheduleStore(harness);
        var retired = Assert.Single(await store.ListByActivationAsync(first.ActivationId!));
        Assert.False(retired.IsActive);

        var schedule = Assert.Single(await store.ListByActivationAsync(second.ActivationId!));
        Assert.True(schedule.IsActive);
        Assert.Equal(TimerStimulus.Hash("PT9M"), schedule.StimulusHash);
        Assert.Equal(superseding.Identity.ArtifactId, schedule.ArtifactId);

        // Exactly one schedule can ever come due for this definition, at any horizon.
        var due = await store.ListDueAsync(Origin.AddYears(1), 10);
        Assert.Equal(schedule.ScheduleId, Assert.Single(due).ScheduleId);

        // Past the OLD cadence but short of the new one: nothing fires, so the retired schedule really is gone
        // rather than merely re-pointed.
        _clock.Advance(TimeSpan.FromMinutes(6));
        await SweepAsync(harness);
        Assert.Null((await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId)).WorkflowState);

        // Past the new one: exactly one run, on the superseding artifact.
        _clock.Advance(TimeSpan.FromMinutes(4));
        await SweepAsync(harness);

        var run = await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        run.AssertWorkflowCompleted();
        Assert.Equal(superseding.Identity.ArtifactId, run.WorkflowState!.PinnedExecutable.ArtifactId);
    }

    /// <summary>
    /// Runs one real pump sweep against the composed engine, on the test clock.
    /// </summary>
    /// <remarks>
    /// Constructed through the pump's direct-construction seam rather than resolved as the hosted recurring task,
    /// so the sweep happens exactly when the test says it does. Every collaborator is the engine's own.
    /// </remarks>
    private async Task SweepAsync(WorkflowExecutionHarness harness)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        var pump = new RecurringTriggerPumpTask(
            scope.ServiceProvider.GetRequiredService<IRecurringTriggerScheduleStore>(),
            scope.ServiceProvider.GetRequiredService<IWorkflowTriggerBindingStore>(),
            scope.ServiceProvider.GetRequiredService<IStimulusRouter>(),
            scope.ServiceProvider.GetRequiredService<IRecurringScheduleCalculator>(),
            Microsoft.Extensions.Options.Options.Create(new RecurringTriggerPumpOptions()),
            _clock,
            NullLogger<RecurringTriggerPumpTask>.Instance);

        await pump.ExecuteAsync(CancellationToken.None);
    }

    private WorkflowExecutable MountTimerArtifact(WorkflowExecutionHarness harness, string interval, string fileName)
    {
        var node = ArtifactClosureFixture.ClrTriggerNode(TriggerNodeId, Timer.ActivityType, nameof(Timer.Interval), interval);
        var executable = ArtifactClosureFixture.Executable(node, DefinitionId);
        ArtifactClosureFixture.Mount(harness.Services, _mount, fileName, ArtifactClosureFixture.Closure(executable));
        return executable;
    }

    private static async Task<WorkflowArtifactReconciliationResult> ReconcileAsync(WorkflowExecutionHarness harness)
    {
        harness.InitializeActivityTypes();
        await using var scope = harness.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowArtifactReconciler>().ReconcileAsync();
    }

    private static IRecurringTriggerScheduleStore ScheduleStore(WorkflowExecutionHarness harness) =>
        harness.Services.GetRequiredService<IRecurringTriggerScheduleStore>();

    private static IWorkflowTriggerBindingStore BindingStore(WorkflowExecutionHarness harness) =>
        harness.Services.GetRequiredService<IWorkflowTriggerBindingStore>();

    private WorkflowExecutionHarness BuildHarness() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new WorkflowsRuntimeTriggersFeature().ConfigureServices(services))
            .WithFeature(services => new WorkflowsRuntimeRecurringTriggersFeature().ConfigureServices(services))
            // The real Timer/Cron providers: the trigger-index stimulus AND the recurring schedule, both derived
            // from the same authored literal, which is what makes the pump's fire route back to this workflow.
            .WithFeature(services => new ActivitiesSchedulingFeature().ConfigureServices(services))
            .WithFeature(services => new JsonWorkflowArtifactReconciliationFeature
            {
                Options =
                {
                    SourceId = SourceId,
                    FolderPath = _mount,
                },
            }.ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
                // Registered last so it wins over the TryAdd'd TimeProvider.System the runtime and recurring
                // features seed: the schedule's first occurrence is anchored at activation time, so the clock must
                // already be the test's before the import runs.
                services.AddSingleton<TimeProvider>(_clock);
            })
            .Build(Enumerable.Range(1, 32).Select(index => $"activity-execution-{index}").ToArray());
}
