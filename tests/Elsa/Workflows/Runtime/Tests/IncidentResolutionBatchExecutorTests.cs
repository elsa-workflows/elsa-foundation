using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Elsa.Workflows.Runtime.Core.Services.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class IncidentResolutionBatchExecutorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Fault_applies_two_incidents_in_stable_order_and_faults_the_workflow_in_one_checkpoint()
    {
        await using var harness = new Harness(IncidentStrategyBuiltIns.FaultReference);
        var second = await harness.AddIncidentAsync("incident-2");
        var first = await harness.AddIncidentAsync("incident-1");

        await harness.ExecuteAsync([second, first]);

        var workflow = await harness.WorkflowStore.FindAsync(Harness.WorkflowExecutionId);
        var firstPersisted = await harness.IncidentStore.FindAsync(Harness.WorkflowExecutionId, "incident-1");
        var secondPersisted = await harness.IncidentStore.FindAsync(Harness.WorkflowExecutionId, "incident-2");
        var commit = Assert.Single(harness.CommitStore.ListCommits()).Commit;
        Assert.Equal(WorkflowExecutionStatus.Faulted, workflow!.Status);
        Assert.All(new[] { firstPersisted!, secondPersisted! }, incident =>
        {
            Assert.Equal(IncidentStatus.Blocking, incident.Status);
            Assert.Equal(IncidentResolutionActionKinds.FaultWorkflow, incident.ResolutionOutcome!.ActionKind);
            Assert.Equal(IncidentStrategyBuiltIns.FaultReference, incident.ResolutionOutcome.Strategy);
        });
        Assert.Equal(["incident-1", "incident-2"], commit.StateChanges.Incidents.Select(change => change.StateId));
        Assert.Equal(RuntimeCheckpointNames.IncidentResolutionBatchApplied, commit.Checkpoint.Name);
        Assert.Equal(RuntimeMetadataKeys.CheckpointRequirementMandatory, commit.Metadata[RuntimeMetadataKeys.CheckpointRequirement]);
    }

    [Fact]
    public async Task Continue_and_wait_preserve_workflow_lifecycle_with_distinct_incident_states()
    {
        await using var continueHarness = new Harness(IncidentStrategyBuiltIns.ContinueWithIncidentsReference);
        var continueIncident = await continueHarness.AddIncidentAsync("incident-continue");

        await continueHarness.ExecuteAsync([continueIncident]);

        var continued = await continueHarness.IncidentStore.FindAsync(Harness.WorkflowExecutionId, "incident-continue");
        Assert.Equal(WorkflowExecutionStatus.Running, (await continueHarness.WorkflowStore.FindAsync(Harness.WorkflowExecutionId))!.Status);
        Assert.Equal(IncidentStatus.Open, continued!.Status);
        Assert.Equal(IncidentResolutionActionKinds.ContinueWithIncidents, continued.ResolutionOutcome!.ActionKind);

        await using var waitHarness = new Harness(new IncidentStrategyReference("Contoso.Wait", "1"));
        var waitIncident = await waitHarness.AddIncidentAsync("incident-wait");

        await waitHarness.ExecuteAsync([waitIncident]);

        var waiting = await waitHarness.IncidentStore.FindAsync(Harness.WorkflowExecutionId, "incident-wait");
        Assert.Equal(WorkflowExecutionStatus.Running, (await waitHarness.WorkflowStore.FindAsync(Harness.WorkflowExecutionId))!.Status);
        Assert.Equal(IncidentStatus.Blocking, waiting!.Status);
        Assert.Equal(IncidentResolutionActionKinds.WaitForIntervention, waiting.ResolutionOutcome!.ActionKind);
    }

    [Fact]
    public async Task Null_strategy_action_falls_back_to_a_fresh_fault_outcome_and_prevents_replay()
    {
        await using var harness = new Harness(new IncidentStrategyReference("Contoso.Null", "1"));
        var incident = await harness.AddIncidentAsync("incident-null");

        await harness.ExecuteAsync([incident]);
        await harness.ExecuteAsync([incident]);

        var persisted = await harness.IncidentStore.FindAsync(Harness.WorkflowExecutionId, incident.IncidentId);
        Assert.Equal(IncidentResolutionActionKinds.FaultWorkflow, persisted!.ResolutionOutcome!.ActionKind);
        Assert.Equal(IncidentResolutionSystemSources.IncidentStrategyFailure, persisted.ResolutionOutcome.SystemSource);
        Assert.Equal("Resolve", persisted.ResolutionOutcome.Metadata["phase"]);
        Assert.Single(harness.CommitStore.ListCommits());
    }

    [Fact]
    public async Task Resolution_batch_checkpoint_is_a_mandatory_coalescing_boundary()
    {
        var checkpoint = new RuntimeCheckpoint(
            "checkpoint-resolution",
            RuntimeCheckpointNames.IncidentResolutionBatchApplied,
            Harness.WorkflowExecutionId,
            Now,
            [],
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory
            });

        var decision = await new CoalescingRuntimeCheckpointPersistencePolicy().DecideAsync(checkpoint);

        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, decision.Mode);
        Assert.Contains(RuntimeCheckpointNames.IncidentResolutionBatchApplied, CoalescingRuntimeCheckpointPersistencePolicy.MandatoryFlushCheckpointNames);
    }

    [Fact]
    public async Task Custom_action_stages_only_guarded_state_metadata_and_registered_safe_intent()
    {
        await using var harness = new Harness(new IncidentStrategyReference("Contoso.SafeIntent", "1"));
        var incident = await harness.AddIncidentAsync("incident-custom");

        Assert.Contains(
            harness.Catalog.List(),
            descriptor => descriptor.Reference.Equals(new IncidentStrategyReference("Contoso.SafeIntent", "1")));
        await harness.ExecuteAsync([incident]);
        await harness.ExecuteAsync([incident]);

        var persisted = await harness.IncidentStore.FindAsync(Harness.WorkflowExecutionId, incident.IncidentId);
        var commit = Assert.Single(harness.CommitStore.ListCommits()).Commit;
        Assert.Equal(IncidentStatus.Resolved, persisted!.Status);
        Assert.Equal("Contoso.NotifyOperator", persisted.ResolutionOutcome!.ActionKind);
        Assert.Equal("custom-value", persisted.ResolutionOutcome.Metadata["custom-key"]);
        var intent = Assert.Single(commit.PostCommitIntents);
        Assert.Equal("Contoso.Notify", intent.Kind);
        Assert.Equal(intent.IntentId, intent.IdempotencyKey);
        Assert.Equal(incident.ActivityExecutionId, intent.ActivityExecutionId);
        Assert.Equal("incident-custom", intent.Payload!.Value.GetProperty("incidentId").GetString());
    }

    [Fact]
    public async Task Strategy_receives_only_the_allowlisted_incident_metadata_projection()
    {
        await using var harness = new Harness(new IncidentStrategyReference("Contoso.MetadataProjection", "1"));
        var incident = await harness.AddIncidentAsync(
            "incident-metadata",
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.CausationKind] = "ActivityFault",
                [RuntimeMetadataKeys.FaultMessage] = "secret message",
                [RuntimeMetadataKeys.ScopedVariableValues] = "secret variables"
            });

        await harness.ExecuteAsync([incident]);

        var persisted = await harness.IncidentStore.FindAsync(Harness.WorkflowExecutionId, incident.IncidentId);
        Assert.Equal("Contoso.MetadataProjected", persisted!.ResolutionOutcome!.ActionKind);
        Assert.Equal("ActivityFault", persisted.ResolutionOutcome.Metadata["projected-causation"]);
    }

    [Theory]
    [InlineData("Contoso.ThrowResolve", "Resolve")]
    [InlineData("Contoso.ThrowExecute", "Execute")]
    [InlineData("Contoso.UnrelatedCancellation", "Execute")]
    public async Task Strategy_and_action_failures_discard_staging_and_apply_fresh_fault_fallback(
        string alias,
        string expectedPhase)
    {
        await using var harness = new Harness(new IncidentStrategyReference(alias, "1"));
        var incident = await harness.AddIncidentAsync($"incident-{alias}");

        await harness.ExecuteAsync([incident]);

        var persisted = await harness.IncidentStore.FindAsync(Harness.WorkflowExecutionId, incident.IncidentId);
        Assert.Equal(IncidentResolutionActionKinds.FaultWorkflow, persisted!.ResolutionOutcome!.ActionKind);
        Assert.Equal(IncidentResolutionSystemSources.IncidentStrategyFailure, persisted.ResolutionOutcome.SystemSource);
        Assert.Equal(expectedPhase, persisted.ResolutionOutcome.Metadata["phase"]);
    }

    [Fact]
    public async Task Custom_action_cannot_spoof_a_reserved_builtin_kind()
    {
        await using var harness = new Harness(new IncidentStrategyReference("Contoso.Spoof", "1"));
        var incident = await harness.AddIncidentAsync("incident-spoof");

        await harness.ExecuteAsync([incident]);

        var persisted = await harness.IncidentStore.FindAsync(Harness.WorkflowExecutionId, incident.IncidentId);
        Assert.Equal(IncidentResolutionActionKinds.FaultWorkflow, persisted!.ResolutionOutcome!.ActionKind);
        Assert.Equal(IncidentResolutionSystemSources.IncidentStrategyFailure, persisted.ResolutionOutcome.SystemSource);
        Assert.Equal("Execute", persisted.ResolutionOutcome.Metadata["phase"]);
    }

    [Fact]
    public async Task Custom_action_kind_is_validated_once_and_the_captured_value_is_persisted()
    {
        await using var harness = new Harness(new IncidentStrategyReference("Contoso.OneShotKind", "1"));
        var incident = await harness.AddIncidentAsync("incident-one-shot-kind");

        await harness.ExecuteAsync([incident]);

        var persisted = await harness.IncidentStore.FindAsync(Harness.WorkflowExecutionId, incident.IncidentId);
        Assert.Equal("Contoso.OneShot", persisted!.ResolutionOutcome!.ActionKind);
    }

    [Fact]
    public async Task Supplied_cancellation_propagates_without_committing_staged_state()
    {
        await using var harness = new Harness(IncidentStrategyBuiltIns.FaultReference);
        var incident = await harness.AddIncidentAsync("incident-cancelled");
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.ExecuteAsync([incident], source.Token).AsTask());

        Assert.Null((await harness.IncidentStore.FindAsync(Harness.WorkflowExecutionId, incident.IncidentId))!.ResolutionOutcome);
        Assert.Empty(harness.CommitStore.ListCommits());
    }

    [Fact]
    public async Task Checkpoint_failure_exposes_no_partial_outcome_or_intent_and_remains_retryable()
    {
        await using var harness = new Harness(
            new IncidentStrategyReference("Contoso.SafeIntent", "1"),
            inner => new FailOnceBeforeCommitStore(inner));
        var incident = await harness.AddIncidentAsync("incident-checkpoint-failure");

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.ExecuteAsync([incident]).AsTask());

        Assert.Null((await harness.IncidentStore.FindAsync(Harness.WorkflowExecutionId, incident.IncidentId))!.ResolutionOutcome);
        Assert.Empty(harness.CommitStore.ListCommits());

        await harness.ExecuteAsync([incident]);

        var persisted = await harness.IncidentStore.FindAsync(Harness.WorkflowExecutionId, incident.IncidentId);
        Assert.Equal("Contoso.NotifyOperator", persisted!.ResolutionOutcome!.ActionKind);
        Assert.Single(Assert.Single(harness.CommitStore.ListCommits()).Commit.PostCommitIntents);
    }

    [Fact]
    public async Task Crash_after_durable_commit_replays_without_duplicate_outcome_or_intent()
    {
        await using var harness = new Harness(
            new IncidentStrategyReference("Contoso.SafeIntent", "1"),
            inner => new ThrowOnceAfterCommitStore(inner));
        var incident = await harness.AddIncidentAsync("incident-crash-after-commit");

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.ExecuteAsync([incident]).AsTask());
        await harness.ExecuteAsync([incident]);

        var persisted = await harness.IncidentStore.FindAsync(Harness.WorkflowExecutionId, incident.IncidentId);
        Assert.Equal("Contoso.NotifyOperator", persisted!.ResolutionOutcome!.ActionKind);
        var durableCommit = Assert.Single(harness.CommitStore.ListCommits()).Commit;
        Assert.Single(durableCommit.PostCommitIntents);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;
        public const string WorkflowExecutionId = "wfexec-strategy";
        public InMemoryWorkflowExecutionStateStore WorkflowStore { get; } = new();
        public InMemoryActivityExecutionStateStore ActivityStore { get; } = new();
        public InMemoryIncidentStateStore IncidentStore { get; } = new();
        public InMemoryWorkflowExecutableStore ExecutableStore { get; } = new();
        public InMemoryRuntimeCheckpointCommitStore CommitStore { get; }
        public IIncidentStrategyCatalog Catalog { get; }
        public IncidentResolutionBatchExecutor Executor { get; }
        public WorkflowExecutionCommandEnvelope Envelope { get; }

        public Harness(
            IncidentStrategyReference reference,
            Func<IRuntimeCheckpointCommitStore, IRuntimeCheckpointCommitStore>? decorateCommitStore = null)
        {
            var services = new ServiceCollection();
            services.AddWorkflowRuntime();
            services.AddIncidentStrategy<WaitStrategy>(new IncidentStrategyDescriptor(new IncidentStrategyReference("Contoso.Wait", "1"), "Wait"));
            services.AddIncidentStrategy<NullStrategy>(new IncidentStrategyDescriptor(new IncidentStrategyReference("Contoso.Null", "1"), "Null"));
            services.AddIncidentStrategy<SafeIntentStrategy>();
            services.AddIncidentStrategy<SpoofStrategy>(new IncidentStrategyDescriptor(new IncidentStrategyReference("Contoso.Spoof", "1"), "Spoof"));
            services.AddIncidentStrategy<ThrowResolveStrategy>(new IncidentStrategyDescriptor(new IncidentStrategyReference("Contoso.ThrowResolve", "1"), "Throw resolve"));
            services.AddIncidentStrategy<ThrowExecuteStrategy>(new IncidentStrategyDescriptor(new IncidentStrategyReference("Contoso.ThrowExecute", "1"), "Throw execute"));
            services.AddIncidentStrategy<UnrelatedCancellationStrategy>(new IncidentStrategyDescriptor(new IncidentStrategyReference("Contoso.UnrelatedCancellation", "1"), "Unrelated cancellation"));
            services.AddIncidentStrategy<OneShotKindStrategy>(new IncidentStrategyDescriptor(new IncidentStrategyReference("Contoso.OneShotKind", "1"), "One-shot kind"));
            services.AddIncidentStrategy<MetadataProjectionStrategy>(new IncidentStrategyDescriptor(new IncidentStrategyReference("Contoso.MetadataProjection", "1"), "Metadata projection"));
            services.AddIncidentStrategySafeIntent(new IncidentStrategySafeIntentDescriptor("Contoso.Notify"));
            _provider = services.BuildServiceProvider(validateScopes: true);
            _scope = _provider.CreateScope();
            Catalog = _provider.GetRequiredService<IIncidentStrategyCatalog>();
            CommitStore = new InMemoryRuntimeCheckpointCommitStore(
                WorkflowStore,
                activityExecutionStateStore: ActivityStore,
                incidentStateStore: IncidentStore,
                rootWriteLeaseManager: PassThroughWorkflowExecutableRootWriteLeaseManager.Instance);
            var committer = new RuntimeCheckpointCommitter(
                new ImmediateRuntimeCheckpointPersistencePolicy(),
                decorateCommitStore?.Invoke(CommitStore) ?? CommitStore);
            Executor = new IncidentResolutionBatchExecutor(
                WorkflowStore,
                ActivityStore,
                IncidentStore,
                ExecutableStore,
                _scope.ServiceProvider.GetRequiredService<IIncidentStrategyResolver>(),
                committer,
                new FixedTimeProvider(Now),
                _scope.ServiceProvider.GetServices<IncidentStrategySafeIntentRegistration>());
            Envelope = new WorkflowExecutionCommandEnvelope(
                "envelope-strategy",
                WorkflowExecutionId,
                new WorkflowExecutionCommand("command-strategy", WorkflowExecutionId, WorkflowExecutionCommandKind.RunSchedulerWork, Now, null, new Dictionary<string, string>()),
                "idempotency-strategy",
                WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
                Now);
            var executable = NewExecutable(reference);
            ExecutableStore.SaveAsync(executable).AsTask().GetAwaiter().GetResult();
            WorkflowStore.SaveAsync(new WorkflowExecutionState(
                WorkflowExecutionId,
                executable.Identity,
                WorkflowExecutionStatus.Running,
                null,
                Now,
                Now,
                Now,
                null,
                null,
                null,
                null,
                new Dictionary<string, string>())).AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask<IncidentState> AddIncidentAsync(
            string incidentId,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            var incident = new IncidentState(
                incidentId,
                WorkflowExecutionId,
                $"activity-{incidentId}",
                "node-1",
                IncidentSeverity.Error,
                IncidentStatus.Blocking,
                null,
                "System.InvalidOperationException",
                "not exposed to strategy",
                Now,
                null,
                metadata ?? new Dictionary<string, string>());
            await IncidentStore.TryAddAsync(incident);
            return incident;
        }

        public ValueTask ExecuteAsync(
            IReadOnlyCollection<IncidentState> incidents,
            CancellationToken cancellationToken = default) =>
            Executor.ExecuteAsync(Envelope, incidents, cancellationToken);

        public ValueTask DisposeAsync()
        {
            _scope.Dispose();
            _provider.Dispose();
            return ValueTask.CompletedTask;
        }

        private static WorkflowExecutable NewExecutable(IncidentStrategyReference strategy) =>
            new(
                new WorkflowExecutableIdentity("artifact-strategy", "definition", "version", "1", "hash"),
                new ExecutableNode(
                    "node-1",
                    "authored-1",
                    "test/activity",
                    "1",
                    "test/descriptor",
                    JsonSerializer.SerializeToElement(new { }),
                    new Dictionary<string, RuntimeInputBinding>(),
                    new Dictionary<string, string>()),
                new Dictionary<string, WorkflowExecutableResumeTarget>(),
                Now,
                new Dictionary<string, string>(),
                inputContract: null,
                dependencies: null,
                runtimeRequirements: null,
                storageDriverRequirements: null,
                incidentStrategy: strategy);
    }

    private sealed class WaitStrategy : IIncidentStrategy
    {
        public ValueTask<IIncidentResolutionAction> ResolveAsync(IncidentStrategyContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(IncidentResolutionActions.WaitForIntervention());
    }

    private sealed class NullStrategy : IIncidentStrategy
    {
        public ValueTask<IIncidentResolutionAction> ResolveAsync(IncidentStrategyContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IIncidentResolutionAction>(null!);
    }

    [IncidentStrategy("Contoso.SafeIntent", "1", DisplayName = "Safe intent")]
    private sealed class SafeIntentStrategy : IIncidentStrategy
    {
        public ValueTask<IIncidentResolutionAction> ResolveAsync(IncidentStrategyContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IIncidentResolutionAction>(new SafeIntentAction(context.Incident.IncidentId));
    }

    private sealed class SafeIntentAction(string incidentId) : IIncidentResolutionAction
    {
        public string Kind => "Contoso.NotifyOperator";

        public ValueTask ExecuteAsync(IncidentResolutionActionContext context, CancellationToken cancellationToken)
        {
            context.ResolveIncident();
            context.AddMetadata("custom-key", "custom-value");
            context.AddPostCommitIntent(new IncidentStrategySafePostCommitIntent(
                "Contoso.Notify",
                JsonSerializer.SerializeToElement(new { incidentId })));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SpoofStrategy : IIncidentStrategy
    {
        public ValueTask<IIncidentResolutionAction> ResolveAsync(IncidentStrategyContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IIncidentResolutionAction>(new SpoofAction());
    }

    private sealed class SpoofAction : IIncidentResolutionAction
    {
        public string Kind => IncidentResolutionActionKinds.ContinueWithIncidents;
        public ValueTask ExecuteAsync(IncidentResolutionActionContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class ThrowResolveStrategy : IIncidentStrategy
    {
        public ValueTask<IIncidentResolutionAction> ResolveAsync(IncidentStrategyContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("resolve failed");
    }

    private sealed class ThrowExecuteStrategy : IIncidentStrategy
    {
        public ValueTask<IIncidentResolutionAction> ResolveAsync(IncidentStrategyContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IIncidentResolutionAction>(new ThrowExecuteAction());
    }

    private sealed class UnrelatedCancellationStrategy : IIncidentStrategy
    {
        public ValueTask<IIncidentResolutionAction> ResolveAsync(IncidentStrategyContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IIncidentResolutionAction>(new UnrelatedCancellationAction());
    }

    private sealed class ThrowExecuteAction : IIncidentResolutionAction
    {
        public string Kind => "Contoso.Throw";
        public ValueTask ExecuteAsync(IncidentResolutionActionContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("execute failed");
    }

    private sealed class UnrelatedCancellationAction : IIncidentResolutionAction
    {
        public string Kind => "Contoso.Cancel";
        public ValueTask ExecuteAsync(IncidentResolutionActionContext context, CancellationToken cancellationToken) =>
            throw new OperationCanceledException(new CancellationToken(canceled: false));
    }

    private sealed class OneShotKindStrategy : IIncidentStrategy
    {
        public ValueTask<IIncidentResolutionAction> ResolveAsync(IncidentStrategyContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IIncidentResolutionAction>(new OneShotKindAction());
    }

    private sealed class OneShotKindAction : IIncidentResolutionAction
    {
        private int _reads;

        public string Kind => Interlocked.Increment(ref _reads) == 1
            ? "Contoso.OneShot"
            : throw new InvalidOperationException("Kind must not be read after validation.");

        public ValueTask ExecuteAsync(IncidentResolutionActionContext context, CancellationToken cancellationToken)
        {
            context.ResolveIncident();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MetadataProjectionStrategy : IIncidentStrategy
    {
        public ValueTask<IIncidentResolutionAction> ResolveAsync(
            IncidentStrategyContext context,
            CancellationToken cancellationToken)
        {
            if (context.Incident.Metadata.ContainsKey(RuntimeMetadataKeys.FaultMessage) ||
                context.Incident.Metadata.ContainsKey(RuntimeMetadataKeys.ScopedVariableValues))
                throw new InvalidOperationException("Sensitive incident metadata reached strategy code.");

            return ValueTask.FromResult<IIncidentResolutionAction>(
                new MetadataProjectionAction(context.Incident.Metadata[RuntimeMetadataKeys.CausationKind]));
        }
    }

    private sealed class MetadataProjectionAction(string causationKind) : IIncidentResolutionAction
    {
        public string Kind => "Contoso.MetadataProjected";

        public ValueTask ExecuteAsync(
            IncidentResolutionActionContext context,
            CancellationToken cancellationToken)
        {
            context.ResolveIncident();
            context.AddMetadata("projected-causation", causationKind);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailOnceBeforeCommitStore(IRuntimeCheckpointCommitStore inner) : IRuntimeCheckpointCommitStore
    {
        private int _attempt;

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _attempt) == 1)
                throw new InvalidOperationException("Simulated checkpoint failure.");
            return inner.CommitAsync(commit, decision, cancellationToken);
        }
    }

    private sealed class ThrowOnceAfterCommitStore(IRuntimeCheckpointCommitStore inner) : IRuntimeCheckpointCommitStore
    {
        private int _attempt;

        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.CommitAsync(commit, decision, cancellationToken);
            if (Interlocked.Increment(ref _attempt) == 1)
                throw new InvalidOperationException("Simulated crash after durable commit.");
            return result;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
