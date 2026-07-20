using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Events.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

public class DesignContractSuiteHarnessTests
{
    [Fact]
    public async Task Activity_reconciliation_scenario_invokes_the_resolved_reconciler()
    {
        var suite = new HarnessActivityDesignContractSuite();

        await suite.Reconciliation_is_idempotent_after_restart();

        Assert.Equal(2, suite.Fixture!.ReconcileCalls);
    }

    [Fact]
    public async Task Atomicity_contract_scenarios_execute_against_the_provider_neutral_harness()
    {
        var suite = new HarnessAtomicityContractSuite();

        await suite.Partial_staging_failure_leaves_no_visible_partial_aggregate();
        await suite.Non_success_provider_decision_rolls_back_all_staged_parts();
        await suite.Cancellation_rolls_back_and_propagates_cancellation();
        await suite.Lost_acknowledgement_after_durable_decision_reconciles_the_authoritative_result_on_retry();
        await suite.Same_stable_operation_key_and_canonical_fingerprint_replay_the_prior_result();
        await suite.Stable_operation_key_reuse_with_a_different_fingerprint_conflicts_without_mutation();
        await suite.Duplicate_delivery_does_not_duplicate_the_domain_outcome();
    }

    [SkippableFact]
    public async Task Legacy_ef_oracle_non_applicable_atomicity_row_skips_before_fixture_creation()
    {
        var suite = new LegacyEfOracleAtomicityContractSuite();

        await suite.Lost_acknowledgement_after_durable_decision_reconciles_the_authoritative_result_on_retry();
    }

    private sealed class HarnessActivityDesignContractSuite : ActivityDesignContractSuite
    {
        public HarnessFixture? Fixture { get; private set; }

        protected override Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default)
        {
            Fixture = new HarnessFixture();
            return Task.FromResult<IDesignPersistenceContractFixture>(Fixture);
        }
    }

    private sealed class HarnessAtomicityContractSuite : DesignAtomicityContractSuite
    {
        protected override DesignPersistenceContractProfile ContractProfile => DesignPersistenceContractProfiles.Target;

        protected override Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IDesignPersistenceContractFixture>(new AtomicityHarnessFixture());
    }

    private sealed class LegacyEfOracleAtomicityContractSuite : DesignAtomicityContractSuite
    {
        protected override DesignPersistenceContractProfile ContractProfile => DesignPersistenceContractProfiles.LegacyEfOracle;

        protected override Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A non-applicable legacy-EF row must skip before it creates a fixture.");
    }

    private sealed class HarnessFixture : IDesignPersistenceContractFixture
    {
        private readonly List<ActivityDefinition> _definitions = [];
        private readonly List<ActivityDefinitionVersion> _versions = [];
        private readonly List<IEvent> _events = [];
        private readonly ServiceProvider _services;
        private readonly HarnessReconciler _reconciler;
        private IReadOnlyCollection<ActivityDefinitionVersion> _candidates = [];

        public HarnessFixture()
        {
            _reconciler = new HarnessReconciler(this);
            _services = new ServiceCollection()
                .AddSingleton<IActivityDefinitionStore>(new HarnessDefinitionStore(_definitions))
                .AddSingleton<IActivityDefinitionVersionStore>(new HarnessVersionStore(_versions))
                .AddSingleton<IActivityVersionReconciler>(_reconciler)
                .BuildServiceProvider();
        }

        public string Provider => "contract-harness";
        public int ReconcileCalls => _reconciler.Calls;

        public IServiceScope CreateScope(string storageScope) => _services.CreateScope();

        public Task RestartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ValidateReadinessAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StageActivityReconciliationCandidatesAsync(
            string storageScope,
            IReadOnlyCollection<ActivityDefinitionVersion> candidates,
            CancellationToken cancellationToken = default)
        {
            _candidates = candidates;
            return Task.CompletedTask;
        }

        public void ClearObservedEvents() => _events.Clear();

        public Task<IReadOnlyList<IEvent>> ReadObservedEventsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IEvent>>(_events.ToArray());

        public Task<IDesignAtomicityFaultLease> ArmAtomicityFaultAsync(
            DesignAtomicityFaultPlan plan,
            CancellationToken cancellationToken = default) =>
            Task.FromException<IDesignAtomicityFaultLease>(new NotSupportedException("The reconciliation harness does not execute atomic-write scenarios."));

        public Task<DesignAtomicityOperationResult> ExecuteAtomicityOperationAsync(
            DesignAtomicityOperationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<DesignAtomicityOperationResult>(new NotSupportedException("The reconciliation harness does not execute atomic-write scenarios."));

        public Task<DesignAtomicitySnapshot> ReadAtomicitySnapshotAsync(
            string storageScope,
            CancellationToken cancellationToken = default) =>
            Task.FromException<DesignAtomicitySnapshot>(new NotSupportedException("The reconciliation harness does not execute atomic-write scenarios."));

        public ValueTask DisposeAsync() => _services.DisposeAsync();

        private sealed class HarnessReconciler(HarnessFixture fixture) : IActivityVersionReconciler
        {
            public int Calls { get; private set; }

            public Task Reconcile(CancellationToken cancellationToken)
            {
                Calls++;
                var reconciling = new OnActivityVersionsReconciling();

                foreach (var candidate in fixture._candidates)
                {
                    reconciling.Versions.Add(candidate);

                    if (fixture._definitions.All(x => x.Id != candidate.DefinitionId))
                        fixture._definitions.Add(candidate.Definition!);
                    if (fixture._versions.All(x => x.Id != candidate.Id))
                        fixture._versions.Add(candidate);
                }

                fixture._events.Add(reconciling);
                return Task.CompletedTask;
            }
        }
    }

    private sealed class HarnessDefinitionStore(List<ActivityDefinition> definitions) : IActivityDefinitionStore
    {
        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(definitions.Single(x => x.Id == id));

        public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(definitions.FirstOrDefault(x =>
                (filter.Id is null || x.Id == filter.Id) &&
                (filter.ActivityTypeKey is null || x.ActivityTypeKey == filter.ActivityTypeKey)));

        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(
            ActivityDefinitionFilter filter,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinition>>(definitions
                .Where(x => filter.Id is null || x.Id == filter.Id)
                .ToArray());

        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(
            string id,
            string activityTypeKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(definitions.FirstOrDefault(x => x.Id == id || x.ActivityTypeKey == activityTypeKey));

        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(definitions.Any(x => x.ActivityTypeKey == activityTypeKey));
    }

    private sealed class HarnessVersionStore(List<ActivityDefinitionVersion> versions) : IActivityDefinitionVersionStore
    {
        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(versions.Single(x => x.Id == versionId));

        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            GetAsync(versionId, cancellationToken);

        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(
            string definitionId,
            string semVerSortKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(versions.FirstOrDefault(x => x.DefinitionId == definitionId && x.SemVerSortKey == semVerSortKey));

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(
            string definitionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(versions.Where(x => x.DefinitionId == definitionId).ToArray());

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(
            IEnumerable<string> definitionIds,
            CancellationToken cancellationToken = default)
        {
            var ids = definitionIds.ToHashSet(StringComparer.Ordinal);
            return Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(versions.Where(x => ids.Contains(x.DefinitionId)).ToArray());
        }

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(versions.ToArray());
    }

    private sealed class AtomicityHarnessFixture : IDesignPersistenceContractFixture
    {
        private const int AggregatePartCount = 2;
        private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
        private readonly Dictionary<string, AtomicityScopeState> _scopes = new(StringComparer.Ordinal);
        private AtomicityFaultLease? _armedFault;

        public string Provider => "atomicity-contract-harness";

        public IServiceScope CreateScope(string storageScope) => _services.CreateScope();

        public Task RestartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ValidateReadinessAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StageActivityReconciliationCandidatesAsync(
            string storageScope,
            IReadOnlyCollection<ActivityDefinitionVersion> candidates,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void ClearObservedEvents()
        {
        }

        public Task<IReadOnlyList<IEvent>> ReadObservedEventsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IEvent>>([]);

        public Task<IDesignAtomicityFaultLease> ArmAtomicityFaultAsync(
            DesignAtomicityFaultPlan plan,
            CancellationToken cancellationToken = default)
        {
            if (_armedFault is not null)
                throw new InvalidOperationException("Only one atomicity fault may be armed at a time.");

            var lease = new AtomicityFaultLease(plan, () => _armedFault = null);
            _armedFault = lease;
            return Task.FromResult<IDesignAtomicityFaultLease>(lease);
        }

        public Task<DesignAtomicityOperationResult> ExecuteAtomicityOperationAsync(
            DesignAtomicityOperationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(request.StorageScope);

            var scope = ScopeState(request.StorageScope);

            if (scope.Ledger.TryGetValue(request.OperationKey.Value, out var existing))
            {
                return Task.FromResult(existing.Fingerprint == request.CanonicalRequestFingerprint.Value
                    ? new DesignAtomicityOperationResult(DesignAtomicityOperationStatus.Replayed, existing.ResultFingerprint)
                    : new DesignAtomicityOperationResult(DesignAtomicityOperationStatus.Conflict, null));
            }

            return Task.FromResult(ExecuteInitialAttempt(request, scope));
        }

        public Task<DesignAtomicitySnapshot> ReadAtomicitySnapshotAsync(
            string storageScope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ScopeState(storageScope).Snapshot());

        public ValueTask DisposeAsync() => _services.DisposeAsync();

        private DesignAtomicityOperationResult ExecuteInitialAttempt(
            DesignAtomicityOperationRequest request,
            AtomicityScopeState scope)
        {
            if (TryTrigger(DesignAtomicityFaultPhase.AfterStagedWrite, out var stagedFault))
                return ApplyPreDecisionFault(stagedFault!);

            if (TryTrigger(DesignAtomicityFaultPhase.BeforeProviderDecision, out var decisionFault))
                return ApplyPreDecisionFault(decisionFault!);

            var resultFingerprint = $"result:{request.StorageScope}:{request.CanonicalRequestFingerprint.Value}";
            scope.VisibleAggregatePartCount = AggregatePartCount;
            scope.CanonicalAggregateStateFingerprint = $"aggregate:{request.StorageScope}:{request.CanonicalRequestFingerprint.Value}";
            scope.AuthoritativeDurableResultFingerprint = resultFingerprint;
            scope.Ledger.Add(request.OperationKey.Value, new(request.CanonicalRequestFingerprint.Value, resultFingerprint));
            scope.PublishedOutcomeCount++;

            if (TryTrigger(DesignAtomicityFaultPhase.AfterDurableDecision, out var acknowledgementFault))
                return ApplyAfterDurableDecisionFault(acknowledgementFault!, resultFingerprint);

            return new(DesignAtomicityOperationStatus.Committed, resultFingerprint);
        }

        private bool TryTrigger(DesignAtomicityFaultPhase phase, out DesignAtomicityFaultPlan? plan)
        {
            plan = null;

            if (_armedFault is null || _armedFault.WasTriggered || _armedFault.Plan.Phase != phase)
                return false;

            _armedFault.Trigger();
            plan = _armedFault.Plan;
            return true;
        }

        private static DesignAtomicityOperationResult ApplyPreDecisionFault(DesignAtomicityFaultPlan plan) =>
            plan.Action switch
            {
                DesignAtomicityFaultAction.Throw => throw new InvalidOperationException("Injected atomicity failure before the durable provider decision."),
                DesignAtomicityFaultAction.Cancel => throw InjectedCancellation("Injected atomicity cancellation before the durable provider decision."),
                DesignAtomicityFaultAction.ReturnNonSuccess => new(DesignAtomicityOperationStatus.Rejected, null),
                _ => throw new ArgumentOutOfRangeException(nameof(plan))
            };

        private static DesignAtomicityOperationResult ApplyAfterDurableDecisionFault(
            DesignAtomicityFaultPlan plan,
            string resultFingerprint) =>
            plan.Action switch
            {
                DesignAtomicityFaultAction.Throw => throw new InvalidOperationException("Injected acknowledgement loss after the durable provider decision."),
                DesignAtomicityFaultAction.Cancel => throw InjectedCancellation("Injected acknowledgement cancellation after the durable provider decision."),
                DesignAtomicityFaultAction.ReturnNonSuccess => new(DesignAtomicityOperationStatus.Rejected, resultFingerprint),
                _ => throw new ArgumentOutOfRangeException(nameof(plan))
            };

        private static OperationCanceledException InjectedCancellation(string message)
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            return new OperationCanceledException(message, cancellation.Token);
        }

        private AtomicityScopeState ScopeState(string storageScope)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(storageScope);

            if (_scopes.TryGetValue(storageScope, out var state))
                return state;

            state = new AtomicityScopeState();
            _scopes.Add(storageScope, state);
            return state;
        }

        private sealed record LedgerEntry(string Fingerprint, string ResultFingerprint);

        private sealed class AtomicityScopeState
        {
            public Dictionary<string, LedgerEntry> Ledger { get; } = new(StringComparer.Ordinal);
            public int VisibleAggregatePartCount { get; set; }
            public int PublishedOutcomeCount { get; set; }
            public string? CanonicalAggregateStateFingerprint { get; set; }
            public string? AuthoritativeDurableResultFingerprint { get; set; }

            public DesignAtomicitySnapshot Snapshot() => new(
                VisibleAggregatePartCount,
                AggregatePartCount,
                Ledger.Count,
                PublishedOutcomeCount,
                CanonicalAggregateStateFingerprint,
                AuthoritativeDurableResultFingerprint);
        }

        private sealed class AtomicityFaultLease(DesignAtomicityFaultPlan plan, Action onDispose) : IDesignAtomicityFaultLease
        {
            private readonly Action _onDispose = onDispose;
            private bool _disposed;

            public DesignAtomicityFaultPlan Plan { get; } = plan;
            public bool WasTriggered { get; private set; }

            public void Trigger() => WasTriggered = true;

            public ValueTask DisposeAsync()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _onDispose();
                }

                return ValueTask.CompletedTask;
            }
        }
    }
}
