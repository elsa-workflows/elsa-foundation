using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Reconciliation.Handlers;
using Elsa.Activities.Design.Reconciliation.Options;
using Elsa.Activities.Design.Reconciliation.Services;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Enums;
using Elsa.Primitives.Persistence;
using Elsa.Primitives.Hosting.Services;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Tests.Integration;

/// <summary>
/// Shared in-memory composition of the real reconciliation pipeline — the real
/// <see cref="CollectActivityVersions"/>, <see cref="ActivityVersionReconciler"/>,
/// hasher, identity generator and descriptor registry — wired over an in-memory catalog so a
/// test can drive a contributed <see cref="IActivityReconciliationSource"/> end-to-end without
/// the EF/event/lock stack. Only the predicate-based <c>Find</c> the reconciler actually uses
/// is implemented; every other query route fails loudly.
/// </summary>
internal static class InMemoryReconcilerHarness
{
    public static ActivityVersionReconciler BuildReconciler(
        CatalogStore store,
        IActivityReconciliationSource source,
        DuplicateHandling duplicateHandling = DuplicateHandling.Skip)
    {
        var identityGenerator = new GuidIdentityGenerator();
        var hasher = new DefaultActivityDefinitionHasher();
        var serializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
        var definitionFactory = new ActivityDefinitionFactory(identityGenerator);
        var versionFactory = new ActivityDefinitionVersionFactory(identityGenerator, hasher);

        var definitionStore = new InMemoryActivityDefinitionStore(store.Definitions);
        var versionStore = new InMemoryActivityDefinitionVersionStore(store.Versions);

        var handler = new CollectActivityVersions(
            definitionStore,
            definitionFactory,
            versionFactory,
            serializer,
            [source]);

        var publisher = new DirectEventPublisher(handler);

        return new ActivityVersionReconciler(
            NullLogger<ActivityVersionReconciler>.Instance,
            hasher,
            publisher,
            Options.Create(new ActivityVersionReconcilerOptions { DuplicateHandling = duplicateHandling }),
            definitionStore,
            versionStore,
            new InMemoryAddActivityDefinitionCommand(store),
            new InMemoryAddVersionCommand(store));
    }

    public sealed class CatalogStore
    {
        public List<ActivityDefinition> Definitions { get; } = [];
        public List<ActivityDefinitionVersion> Versions { get; } = [];
    }

    public sealed class InMemorySource(string sourceId, string sourceKind, params ActivityVersionReconciliationModel[] models) : IActivityReconciliationSource
    {
        public string SourceId => sourceId;
        public string SourceKind => sourceKind;
        public ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IEnumerable<ActivityVersionReconciliationModel>>(models);
    }

    private sealed class DirectEventPublisher(IEventHandler<ActivityVersionsReconciling> handler) : IInlineEventPublisher
    {
        public async Task Publish(IEvent @event, CancellationToken cancellationToken = default)
        {
            if (@event is ActivityVersionsReconciling reconciling)
                await handler.Handle(reconciling, cancellationToken);
        }
    }

    private sealed class GuidIdentityGenerator : IIdentityGenerator
    {
        public string Generate() => Guid.NewGuid().ToString("N");
    }

    private sealed class InMemoryAddActivityDefinitionCommand(CatalogStore store) : IAddActivityDefinitionCommand
    {
        public Task<ActivityDefinitionCreated> Execute(
            DesignOperationKey operationKey,
            ActivityDefinition definition,
            ActivityDefinitionVersion version,
            CancellationToken cancellation)
        {
            store.Definitions.Add(definition);
            store.Versions.Add(version);
            return Task.FromResult(new ActivityDefinitionCreated(definition.Id, version.Id, version.Version, version.Hash));
        }
    }

    private sealed class InMemoryAddVersionCommand(CatalogStore store) : IAddActivityDefinitionVersionCommand
    {
        public Task<ActivityDefinitionVersionAdded> Execute(
            DesignOperationKey operationKey,
            ActivityDefinitionVersion entity,
            CancellationToken cancellationToken = default)
        {
            store.Versions.Add(entity);
            return Task.FromResult(new ActivityDefinitionVersionAdded(entity.DefinitionId, entity.Id, entity.Version, entity.Hash));
        }
    }

    private sealed class InMemoryActivityDefinitionStore(List<ActivityDefinition> items) : IActivityDefinitionStore
    {
        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(items.Single(x => x.Id == id));

        public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(Apply(items, filter).FirstOrDefault());

        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinition?>(items.FirstOrDefault(x => x.Id == id || x.ActivityTypeKey == activityTypeKey));

        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(items.Any(x => x.ActivityTypeKey == activityTypeKey));

        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinition>>(Apply(items, filter).ToList());

        private static IEnumerable<ActivityDefinition> Apply(IEnumerable<ActivityDefinition> items, ActivityDefinitionFilter filter)
        {
            IEnumerable<ActivityDefinition> query = items;

            if (filter.Id is not null)
                query = query.Where(x => string.Equals(x.Id, filter.Id, StringComparison.Ordinal));

            if (filter.Ids is not null)
            {
                var ids = filter.Ids.ToHashSet(StringComparer.Ordinal);
                query = query.Where(x => ids.Contains(x.Id));
            }

            if (filter.ActivityTypeKey is not null)
                query = query.Where(x => string.Equals(x.ActivityTypeKey, filter.ActivityTypeKey, StringComparison.Ordinal));

            if (filter.ActivityTypeKeys is not null)
            {
                var keys = filter.ActivityTypeKeys.ToHashSet(StringComparer.Ordinal);
                query = query.Where(x => keys.Contains(x.ActivityTypeKey));
            }

            if (filter.Category is not null)
                query = query.Where(x => string.Equals(x.Category, filter.Category, StringComparison.Ordinal));

            if (filter.DisplayName is not null)
                query = query.Where(x => string.Equals(x.DisplayName, filter.DisplayName, StringComparison.Ordinal));

            if (filter.Description is not null)
                query = query.Where(x => x.Description?.Contains(filter.Description, StringComparison.OrdinalIgnoreCase) == true);

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                query = query.Where(x =>
                    x.Id.Contains(filter.SearchTerm, StringComparison.OrdinalIgnoreCase)
                    || x.ActivityTypeKey.Contains(filter.SearchTerm, StringComparison.OrdinalIgnoreCase)
                    || x.Category?.Contains(filter.SearchTerm, StringComparison.OrdinalIgnoreCase) == true
                    || x.DisplayName?.Contains(filter.SearchTerm, StringComparison.OrdinalIgnoreCase) == true
                    || x.Description?.Contains(filter.SearchTerm, StringComparison.OrdinalIgnoreCase) == true);
            }

            return query;
        }
    }

    private sealed class InMemoryActivityDefinitionVersionStore(List<ActivityDefinitionVersion> items) : IActivityDefinitionVersionStore
    {
        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(items.Single(x => x.Id == versionId));

        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(items.Single(x => x.Id == versionId));

        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(items.FirstOrDefault(x => x.DefinitionId == definitionId && x.SemVerSortKey == semVerSortKey));

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(items.Where(x => x.DefinitionId == definitionId).ToList());

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default)
        {
            var idSet = definitionIds.ToHashSet();
            return Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(items.Where(x => idSet.Contains(x.DefinitionId)).ToList());
        }

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(items.ToList());
    }

}
