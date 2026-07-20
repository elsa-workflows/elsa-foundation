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

    private sealed class HarnessActivityDesignContractSuite : ActivityDesignContractSuite
    {
        public HarnessFixture? Fixture { get; private set; }

        protected override Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default)
        {
            Fixture = new HarnessFixture();
            return Task.FromResult<IDesignPersistenceContractFixture>(Fixture);
        }
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
}
