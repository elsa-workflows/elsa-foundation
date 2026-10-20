using CShells.Lifecycle;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Tests.Support;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static Elsa.Activities.Compensation.Tests.Support.CompensationGraph;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>
/// Crash-and-restart coverage of compensation recovery (spec 250 quickstart, injection points P1–P4) in every
/// persistence mode. One SQLite file is the durable store that survives the crash; the <see cref="Participant"/> is the
/// external system, shared by both host generations. The first host stops at the injection point; a fresh host over the
/// same file drives the run on with a resumption sweep.
/// </summary>
public sealed class CompensationScopeRestartTests : IDisposable
{
    private const string WorkflowExecutionId = "wfexec-compensation-restart";
    private const string RecoverySigningKey = "compensation-restart-recovery-signing-key-32";
    private const string HierarchySigningKey = "compensation-restart-hierarchy-signing-key-32";
    private static readonly WorkflowExecutionPartition DefaultPartition = new(PersistenceScope.DefaultValue);
    private static readonly string[] ExpectedEffects = ["reserve-inventory", "reserve-delivery", "cancel-delivery", "release-inventory"];

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly string _databasePath = Path.Join(Path.GetTempPath(), $"elsa-compensation-restart-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var file in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
            File.Delete(file);
    }

    public static TheoryData<string, PersistenceMode> AfterEffectPoints()
    {
        var data = new TheoryData<string, PersistenceMode>();
        foreach (var mode in Enum.GetValues<PersistenceMode>())
        foreach (var node in new[] { "reserve-delivery", "cancel-delivery", "release-inventory" })
            data.Add(node, mode);
        return data;
    }

    /// <summary>P1, P2 and P4: the host stops after the participant applied the effect and before the completion commits.</summary>
    [Theory]
    [MemberData(nameof(AfterEffectPoints))]
    public async Task A_crash_after_an_effect_repeats_the_call_with_the_same_invocation_id_and_recovery_finishes_once(string killedNode, PersistenceMode mode)
    {
        var participant = new Participant().Rejecting("confirm-order").KillingHostAfterEffectOf(killedNode);

        await using (var first = await StartHostAsync(participant, mode))
        {
            var reference = await first.PublishAsync(Executable(ReservationJourney()), "source-compensation-restart");
            await StartUntilTheHostStopsAsync(first, reference);

            var beforeRestart = await first.ReadRunAsync(WorkflowExecutionId);
            Assert.DoesNotContain(beforeRestart.ActivityStates, state =>
                state.Execution.ExecutableNodeId == killedNode && state.Status == ActivityExecutionStatus.Completed);
        }

        await using var second = await StartHostAsync(participant, mode);
        await RecoverAsync(second);

        var run = await second.ReadRunAsync(WorkflowExecutionId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Equal(ExpectedEffects, participant.EffectOrder);

        // The killed node was called again after the restart, with the invocation id of the call whose effect landed.
        var calls = participant.Calls.Where(call => call.NodeId == killedNode).ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Equal(calls[0].InvocationId, calls[1].InvocationId);
        Assert.Single(run.States(killedNode), state => state.Status == ActivityExecutionStatus.Completed);
    }

    /// <summary>P3: the host stops between two handlers: the second one is claimed but its effect never happened.</summary>
    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    [InlineData(PersistenceMode.Ephemeral)]
    public async Task A_crash_between_handlers_runs_the_next_handler_exactly_once(PersistenceMode mode)
    {
        var participant = new Participant().Rejecting("confirm-order").KillingHostBeforeEffectOf("release-inventory");

        await using (var first = await StartHostAsync(participant, mode))
        {
            var reference = await first.PublishAsync(Executable(ReservationJourney()), "source-compensation-restart");
            await StartUntilTheHostStopsAsync(first, reference);
            Assert.DoesNotContain("release-inventory", participant.EffectOrder);
        }

        await using var second = await StartHostAsync(participant, mode);
        await RecoverAsync(second);

        var run = await second.ReadRunAsync(WorkflowExecutionId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Equal(ExpectedEffects, participant.EffectOrder);
        Assert.Single(participant.Calls, call => call.NodeId == "release-inventory");
        Assert.Single(participant.Calls, call => call.NodeId == "cancel-delivery");
    }

    /// <summary>
    /// Spec 251 injection point 1: the host stops after a forward effect whose response was lost, before the fault commits.
    /// The step runs again with its operation key; the participant applies nothing twice and recovery leaves no reservation.
    /// </summary>
    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    [InlineData(PersistenceMode.Ephemeral)]
    public async Task A_crash_after_a_lost_response_reapplies_nothing_and_recovery_leaves_no_reservation(PersistenceMode mode)
    {
        var participant = new Participant().Rejecting("confirm-order")
            .LosingResponseAfterEffectOf("reserve-delivery").KillingHostAfterEffectOf("reserve-delivery");

        await using (var first = await StartHostAsync(participant, mode))
        {
            var reference = await first.PublishAsync(Executable(ReservationJourney()), "source-compensation-restart");
            await StartUntilTheHostStopsAsync(first, reference);
        }

        await using var second = await StartHostAsync(participant, mode);
        await RecoverAsync(second);

        var run = await second.ReadRunAsync(WorkflowExecutionId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Equal(ExpectedEffects, participant.EffectOrder);
        Assert.Empty(participant.Present);
        Assert.Single(participant.Calls.Where(call => call.NodeId == "reserve-delivery").Select(call => call.OperationKey).Distinct());
    }

    /// <summary>Spec 251 injection point 2: the host stops after a failed handler attempt commits and before the next runs.</summary>
    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    [InlineData(PersistenceMode.Ephemeral)]
    public async Task A_crash_between_handler_attempts_runs_the_next_attempt_once_with_the_same_operation_key(PersistenceMode mode)
    {
        var participant = new Participant().Rejecting("confirm-order")
            .FailingTimes("cancel-delivery", 1).KillingHostBeforeCall("cancel-delivery", 2);

        await using (var first = await StartHostAsync(participant, mode))
        {
            var reference = await first.PublishAsync(Executable(ReservationJourney()), "source-compensation-restart");
            await StartUntilTheHostStopsAsync(first, reference);
            Assert.DoesNotContain("cancel-delivery", participant.EffectOrder);
        }

        await using var second = await StartHostAsync(participant, mode);
        await RecoverAsync(second);

        var run = await second.ReadRunAsync(WorkflowExecutionId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Equal(ExpectedEffects, participant.EffectOrder);
        var cancels = participant.Calls.Where(call => call.NodeId == "cancel-delivery").ToArray();
        Assert.Equal(2, cancels.Length);
        Assert.Single(cancels.Select(call => call.OperationKey).Distinct());
        Assert.Equal(2, run.States("cancel-delivery").Count);
    }

    /// <summary>Spec 251 injection point 3: a scope waiting for intervention survives a restart without another attempt.</summary>
    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    [InlineData(PersistenceMode.Ephemeral)]
    public async Task A_scope_waiting_for_intervention_stays_waiting_across_a_restart(PersistenceMode mode)
    {
        var participant = new Participant().Rejecting("confirm-order", "cancel-delivery");

        await using (var first = await StartHostAsync(participant, mode))
        {
            var reference = await first.PublishAsync(Executable(ReservationJourney()), "source-compensation-restart");
            await StartUntilTheHostStopsAsync(first, reference);
        }

        await using var second = await StartHostAsync(participant, mode);
        await RecoverAsync(second);

        var run = await second.ReadRunAsync(WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);
        // The three attempts from before the restart, and no fourth after it.
        Assert.Equal(3, run.States("cancel-delivery").Count);
        Assert.DoesNotContain(participant.Calls, call => call.NodeId == "release-inventory");
    }

    /// <summary>
    /// A republish while recovery is in flight does not change the run's handlers: recovery finishes with the handler
    /// of the executable the run started on, not the one published since.
    /// </summary>
    [Fact]
    public async Task A_republish_mid_recovery_keeps_the_handlers_of_the_pinned_executable()
    {
        var participant = new Participant().Rejecting("confirm-order").KillingHostAfterEffectOf("cancel-delivery");

        await using (var first = await StartHostAsync(participant, PersistenceMode.Durable))
        {
            var reference = await first.PublishAsync(Executable(ReservationJourney()), "source-compensation-restart");
            await StartUntilTheHostStopsAsync(first, reference);
        }

        await using var second = await StartHostAsync(participant, PersistenceMode.Durable);
        var republished = Scope(
            new Step("Reserve inventory", Participant("reserve-inventory"), Participant("release-inventory-v2")),
            new Step("Reserve delivery", Participant("reserve-delivery"), Participant("cancel-delivery")),
            new Step("Confirm order", Participant("confirm-order")));
        // The next version of the same definition: a new artifact, reachable through its own source reference.
        var nextVersion = new WorkflowExecutableIdentity("artifact-2", WorkflowExecutionHarness.Identity.DefinitionId, "version-2", "2.0.0", "sha256:test-2");
        await second.PublishAsync(WorkflowExecutionHarness.NewExecutable(republished, nextVersion), "source-compensation-republished");
        await RecoverAsync(second);

        var run = await second.ReadRunAsync(WorkflowExecutionId);
        run.AssertWorkflowCompleted();
        run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Equal(ExpectedEffects, participant.EffectOrder);
        Assert.DoesNotContain(participant.Calls, call => call.NodeId == "release-inventory-v2");
    }

    // The stopped host's work-item and ownership leases outlive it. A restarted process waits them out; here the shared
    // clock moves past them before the new host sweeps.
    private async Task RecoverAsync(WorkflowExecutionHarness host)
    {
        _clock.Advance(TimeSpan.FromHours(1));
        await host.SweepUntilQuietAsync();
    }

    private static async Task StartUntilTheHostStopsAsync(WorkflowExecutionHarness host, WorkflowExecutableSourceReference reference)
    {
        try
        {
            await host.StartPublishedAsync(reference, WorkflowExecutionId, partition: DefaultPartition);
        }
        catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
        {
            // The host stopped during the pass, as a process shutdown stops it.
        }
    }

    private async Task<WorkflowExecutionHarness> StartHostAsync(
        Participant participant,
        PersistenceMode mode,
        Action<IServiceCollection>? configure = null)
    {
        var host = WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesCompensationFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services
                    .AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                    {
                        Provider = "Sqlite",
                        ConnectionString = $"Data Source={_databasePath};Pooling=False",
                        RecoveryContinuationSigningKey = RecoverySigningKey,
                        HierarchyCursorSigningKey = HierarchySigningKey
                    })
                    .AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
                services.AddPersistenceModes(options =>
                {
                    options.Mode = mode;
                    options.RecordCompletion = true;
                });
                services.AddSingleton(participant);
                HostKillSwitch.Install(services, new HostKillSwitch());
                services.Replace(ServiceDescriptor.Singleton<IRuntimeExecutionIdGenerator>(_ => new ShortRuntimeExecutionIdGenerator(TimeProvider.System)));
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_clock);
                configure?.Invoke(services);
            })
            .Build();

        foreach (var initializer in host.Services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        host.InitializeActivityTypes();
        return host;
    }
}
