using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Enums;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Reconciliation.Core;
using Elsa.Workflows.Design.Reconciliation.Options;
using Elsa.Workflows.Design.Reconciliation.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation;

/// <summary>
/// Branch coverage for <see cref="WorkflowsVersionReconciler"/>. Cases: fresh definition +
/// first version → both adds called; existing definition with new version number → only
/// version-add called; existing (DefId, Version) → duplicate-handling kicks in
/// (Skip default + Throw override).
/// </summary>
public sealed class WorkflowsVersionReconcilerTests
{
    [Fact]
    public async Task Fresh_definition_calls_both_add_definition_and_add_version()
    {
        var incoming = BuildIncomingVersion(definitionId: "wf-new", version: "1.0.0");
        var sender = new CapturingSender { ToContribute = [incoming] };
        var defs = new StubQueries<WorkflowDefinition>();
        var versions = new StubQueries<WorkflowDefinitionVersion>();
        var addDef = new SpyAddCommand<WorkflowDefinition>();
        var addVer = new SpyAddCommand<WorkflowDefinitionVersion>();

        var reconciler = NewReconciler(sender, defs, versions, addDef, addVer, DuplicateHandling.Skip);
        await reconciler.Reconcile(CancellationToken.None);

        Assert.Single(addDef.Added);
        Assert.Equal("wf-new", addDef.Added[0].Id);
        Assert.Single(addVer.Added);
        Assert.Equal("wf-new", addVer.Added[0].DefinitionId);
        Assert.Equal("1.0.0", addVer.Added[0].Version);
    }

    [Fact]
    public async Task Existing_definition_new_version_calls_add_version_only()
    {
        var incoming = BuildIncomingVersion(definitionId: "wf-existing", version: "2.0.0");
        var existingDef = new WorkflowDefinition { Id = "wf-existing", Name = "Existing" };

        var defs = new StubQueries<WorkflowDefinition>().With(existingDef);
        var versions = new StubQueries<WorkflowDefinitionVersion>(); // No existing versions.
        var addDef = new SpyAddCommand<WorkflowDefinition>();
        var addVer = new SpyAddCommand<WorkflowDefinitionVersion>();

        var reconciler = NewReconciler(
            new CapturingSender { ToContribute = [incoming] },
            defs, versions, addDef, addVer, DuplicateHandling.Skip);
        await reconciler.Reconcile(CancellationToken.None);

        Assert.Empty(addDef.Added);
        Assert.Single(addVer.Added);
        Assert.Equal("2.0.0", addVer.Added[0].Version);
    }

    [Fact]
    public async Task Duplicate_version_with_Skip_handling_does_not_add()
    {
        var incoming = BuildIncomingVersion(definitionId: "wf-dup", version: "1.0.0");
        var existingDef = new WorkflowDefinition { Id = "wf-dup", Name = "Dup" };
        var existingVersion = new WorkflowDefinitionVersion("wf-dup", "1.0.0");

        var defs = new StubQueries<WorkflowDefinition>().With(existingDef);
        var versions = new StubQueries<WorkflowDefinitionVersion>().With(existingVersion);
        var addDef = new SpyAddCommand<WorkflowDefinition>();
        var addVer = new SpyAddCommand<WorkflowDefinitionVersion>();

        var reconciler = NewReconciler(
            new CapturingSender { ToContribute = [incoming] },
            defs, versions, addDef, addVer, DuplicateHandling.Skip);
        await reconciler.Reconcile(CancellationToken.None);

        Assert.Empty(addDef.Added);
        Assert.Empty(addVer.Added);
    }

    [Fact]
    public async Task Duplicate_version_with_Throw_handling_throws()
    {
        var incoming = BuildIncomingVersion(definitionId: "wf-dup", version: "1.0.0");
        var existingDef = new WorkflowDefinition { Id = "wf-dup", Name = "Dup" };
        var existingVersion = new WorkflowDefinitionVersion("wf-dup", "1.0.0");

        var defs = new StubQueries<WorkflowDefinition>().With(existingDef);
        var versions = new StubQueries<WorkflowDefinitionVersion>().With(existingVersion);

        var reconciler = NewReconciler(
            new CapturingSender { ToContribute = [incoming] },
            defs, versions,
            new SpyAddCommand<WorkflowDefinition>(), new SpyAddCommand<WorkflowDefinitionVersion>(),
            DuplicateHandling.Throw);

        await Assert.ThrowsAsync<InvalidOperationException>(() => reconciler.Reconcile(CancellationToken.None));
    }

    private static WorkflowsVersionReconciler NewReconciler(
        IEventPublisher sender,
        IQueries<WorkflowDefinition> defs,
        IQueries<WorkflowDefinitionVersion> versions,
        IAddCommand<WorkflowDefinition> addDef,
        IAddCommand<WorkflowDefinitionVersion> addVer,
        DuplicateHandling duplicateHandling)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new WorkflowVersionReconcilerOptions { DuplicateHandling = duplicateHandling });
        return new WorkflowsVersionReconciler(
            NullLogger<WorkflowsVersionReconciler>.Instance,
            sender,
            options,
            defs,
            versions,
            addDef,
            addVer);
    }

    private static IWorkflowDefinitionVersion BuildIncomingVersion(string definitionId, string version) => new StubIncomingVersion
    {
        Id = string.Empty,
        Version = version,
        DefinitionId = definitionId,
        DefinitionFacade = new StubIncomingDefinition { Id = definitionId, Name = "Stub" },
        State = new WorkflowDefinitionState([], null, [], [], null, null),
    };

    private sealed class StubIncomingDefinition : IWorkflowDefinition
    {
        public string Id { get; init; } = default!;
        public string Name { get; init; } = default!;
        public string? Description { get; init; }
        public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UtcNow;
        public IWorkflowDefinition ShallowClone() => this;
    }

    private sealed class StubIncomingVersion : IWorkflowDefinitionVersion
    {
        public string Id { get; init; } = default!;
        public string Version { get; init; } = default!;
        public string DefinitionId { get; init; } = default!;
        public StubIncomingDefinition DefinitionFacade { get; init; } = default!;
        public IWorkflowDefinition Definition => DefinitionFacade;
        public WorkflowDefinitionState State { get; init; } = default!;
        public string? StateSource => null;
        public DateTimeOffset? SourceCreatedAt => null;
        public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UtcNow;
    }

    private sealed class CapturingSender : IEventPublisher
    {
        public List<IWorkflowDefinitionVersion> ToContribute { get; init; } = new();
        public Task Publish(IEvent @event, IEventPublishingStrategy? strategy = null, CancellationToken cancellationToken = default)
        {
            if (@event is OnWorkflowVersionsReconciling rec)
                foreach (var v in ToContribute)
                    rec.Versions.Add(v);
            return Task.CompletedTask;
        }
    }

    private sealed class StubQueries<TEntity> : IQueries<TEntity> where TEntity : Entity
    {
        private readonly List<TEntity> _items = new();
        public StubQueries<TEntity> With(TEntity item) { _items.Add(item); return this; }

        public Task<TEntity?> Find(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.AsQueryable().FirstOrDefault(predicate));

        public Task<bool> Any(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.AsQueryable().Any(predicate));

        public Task<IEnumerable<TEntity>> Query<TProp>(IFilter<TEntity> filter, Elsa.Primitives.Persistence.OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default)
        {
            var q = filter.Apply(_items.AsQueryable());
            var ordered = order.Direction == Elsa.Primitives.Persistence.OrderDirection.Ascending
                ? q.OrderBy(order.KeySelector)
                : q.OrderByDescending(order.KeySelector);
            return Task.FromResult<IEnumerable<TEntity>>(ordered.ToList());
        }

        // Other IQueries members are not invoked by the reconciler; throw if hit.
        private const string Unused = "Not exercised by reconciler tests.";
        public Task<TEntity?> Find(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<TEntity?> Find(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<TEntity?> Find<TProperty>(IFilter<TEntity> filter, System.Linq.Expressions.Expression<Func<TEntity, TProperty>> include, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<TEntity?> Find<TProperty>(IFilter<TEntity> filter, IEnumerable<System.Linq.Expressions.Expression<Func<TEntity, TProperty>>> include, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<IEnumerable<TEntity>> FindMany(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<IEnumerable<TEntity>> FindMany(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<IEnumerable<TEntity>> FindMany<TProp>(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, Elsa.Primitives.Persistence.OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<Elsa.Primitives.Persistence.Page<TEntity>> FindMany(System.Linq.Expressions.Expression<Func<TEntity, bool>>? predicate, Elsa.Primitives.Persistence.PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<Elsa.Primitives.Persistence.Page<TEntity>> FindMany(IFilter<TEntity> filter, Elsa.Primitives.Persistence.PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<Elsa.Primitives.Persistence.Page<TEntity>> FindMany<TProp>(System.Linq.Expressions.Expression<Func<TEntity, bool>>? predicate, Elsa.Primitives.Persistence.OrderDefinition<TEntity, TProp> order, Elsa.Primitives.Persistence.PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<IEnumerable<TEntity>> List(CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<IEnumerable<TEntity>> Query(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<IEnumerable<TEntity>> Query(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<IEnumerable<TEntity>> Query<TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Elsa.Primitives.Persistence.OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<IEnumerable<TResult>> Query<TResult>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, System.Linq.Expressions.Expression<Func<TEntity, TResult>> selector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<IEnumerable<TResult>> Query<TResult>(IFilter<TEntity> filter, System.Linq.Expressions.Expression<Func<TEntity, TResult>> selector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<IEnumerable<TResult>> Query<TResult, TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, System.Linq.Expressions.Expression<Func<TEntity, TResult>> selector, Elsa.Primitives.Persistence.OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<IEnumerable<TResult>> Query<TResult, TProp>(IFilter<TEntity> filter, System.Linq.Expressions.Expression<Func<TEntity, TResult>> selector, Elsa.Primitives.Persistence.OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<long> Count(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<bool> Any(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<bool> Any(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<long> Count(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<long> Count(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<long> Count<TProperty>(IFilter<TEntity> filter, System.Linq.Expressions.Expression<Func<TEntity, TProperty>> propertySelector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<long> Count<TProperty>(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, System.Linq.Expressions.Expression<Func<TEntity, TProperty>> propertySelector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
    }

    private sealed class SpyAddCommand<TEntity> : IAddCommand<TEntity> where TEntity : Entity
    {
        public List<TEntity> Added { get; } = new();
        public Task Add(TEntity entity, CancellationToken cancellationToken = default) { Added.Add(entity); return Task.CompletedTask; }
    }

    private sealed class SequentialIdGenerator : IIdentityGenerator
    {
        private int _i;
        public string Generate() => $"gen-{Interlocked.Increment(ref _i)}";
    }
}
