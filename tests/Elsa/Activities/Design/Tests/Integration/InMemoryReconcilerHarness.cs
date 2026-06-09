using System.Linq.Expressions;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Reconciliation.Handlers;
using Elsa.Activities.Design.Reconciliation.Options;
using Elsa.Activities.Design.Reconciliation.Services;
using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Entities;
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

        var definitionQueries = new InMemoryQueries<ActivityDefinition>(store.Definitions);
        var versionQueries = new InMemoryQueries<ActivityDefinitionVersion>(store.Versions);

        var handler = new CollectActivityVersions(
            definitionQueries,
            definitionFactory,
            versionFactory,
            serializer,
            [source]);

        var publisher = new DirectEventPublisher(handler);

        return new ActivityVersionReconciler(
            NullLogger<ActivityVersionReconciler>.Instance,
            publisher,
            Options.Create(new ActivityVersionReconcilerOptions { DuplicateHandling = duplicateHandling }),
            definitionQueries,
            versionQueries,
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

    private sealed class DirectEventPublisher(IEventHandler<OnActivityVersionsReconciling> handler) : IEventPublisher
    {
        public async Task Publish(IEvent @event, IEventPublishingStrategy? strategy = null, CancellationToken cancellationToken = default)
        {
            if (@event is OnActivityVersionsReconciling reconciling)
                await handler.Handle(reconciling, cancellationToken);
        }
    }

    private sealed class GuidIdentityGenerator : IIdentityGenerator
    {
        public string Generate() => Guid.NewGuid().ToString("N");
    }

    private sealed class InMemoryAddActivityDefinitionCommand(CatalogStore store) : IAddActivityDefinitionCommand
    {
        public Task Execute(ActivityDefinition definition, ActivityDefinitionVersion version, CancellationToken cancellation)
        {
            store.Definitions.Add(definition);
            store.Versions.Add(version);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryAddVersionCommand(CatalogStore store) : IAddCommand<ActivityDefinitionVersion>
    {
        public Task Add(ActivityDefinitionVersion entity, CancellationToken cancellationToken = default)
        {
            store.Versions.Add(entity);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryQueries<TEntity>(List<TEntity> items) : IQueries<TEntity> where TEntity : Entity
    {
        private const string Msg = "InMemoryQueries: only predicate-based Find is supported in this harness.";

        public Task<TEntity?> Find(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult(items.AsQueryable().FirstOrDefault(predicate));

        public Task<TEntity?> Find(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<TEntity?> Find(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<TEntity?> Find<TProperty>(IFilter<TEntity> filter, Expression<Func<TEntity, TProperty>> include, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<TEntity?> Find<TProperty>(IFilter<TEntity> filter, IEnumerable<Expression<Func<TEntity, TProperty>>> include, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> FindMany(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> FindMany(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> FindMany<TProp>(Expression<Func<TEntity, bool>> predicate, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<Page<TEntity>> FindMany(Expression<Func<TEntity, bool>>? predicate, PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<Page<TEntity>> FindMany(IFilter<TEntity> filter, PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<Page<TEntity>> FindMany<TProp>(Expression<Func<TEntity, bool>>? predicate, OrderDefinition<TEntity, TProp> order, PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> List(CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> Query(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> Query(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> Query<TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TResult>> Query<TResult>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Expression<Func<TEntity, TResult>> selector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TResult>> Query<TResult>(IFilter<TEntity> filter, Expression<Func<TEntity, TResult>> selector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> Query<TProperty>(IFilter<TEntity> filter, OrderDefinition<TEntity, TProperty> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TResult>> Query<TResult, TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Expression<Func<TEntity, TResult>> selector, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TResult>> Query<TResult, TProp>(IFilter<TEntity> filter, Expression<Func<TEntity, TResult>> selector, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<long> Count(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<bool> Any(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<bool> Any(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<bool> Any(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<long> Count(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<long> Count(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<long> Count<TProperty>(IFilter<TEntity> filter, Expression<Func<TEntity, TProperty>> propertySelector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<long> Count<TProperty>(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, TProperty>> propertySelector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    }
}
