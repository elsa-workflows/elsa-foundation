using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Reconciliation;
using Elsa.Workflows.Design.Reconciliation.Contracts;
using Elsa.Workflows.Design.Reconciliation.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation;

/// <summary>
/// Framework §2.23.1 registration smoke for <see cref="WorkflowsDesignReconciliationFeature"/>.
/// Concrete features extend the abstract base and override <c>Sources</c>; here we instantiate
/// a minimal subclass and verify every claimed service resolves from the built provider.
/// </summary>
public sealed class WorkflowsDesignReconciliationFeatureRegistrationTests
{
    [Fact]
    public void Feature_registers_reconciler_and_handler()
    {
        var services = MinimalServices();
        new TestWorkflowsDesignReconciliationFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<IWorkflowVersionReconciler>());

        var handlers = scope.ServiceProvider.GetServices<IDomainEventHandler>().ToList();
        Assert.Contains(handlers, h => h is IDomainEventHandler<OnWorkflowVersionsReconciling>);
    }

    [Fact]
    public void Sources_set_on_feature_register_as_singletons()
    {
        var services = MinimalServices();
        var source = new StubSource("Test", "test-1");
        new TestWorkflowsDesignReconciliationFeature
        {
            Sources = [source]
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var registered = provider.GetServices<IWorkflowReconciliationSource>().ToList();

        Assert.Single(registered);
        Assert.Same(source, registered[0]);
    }

    private static ServiceCollection MinimalServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddLogging();
        services.AddSingleton<IIdentityGenerator, StubIdentityGenerator>();
        services.AddSingleton<IDomainEventSender, StubDomainEventSender>();
        services.AddSingleton<IQueries<WorkflowDefinition>, ThrowingQueries<WorkflowDefinition>>();
        services.AddSingleton<IQueries<WorkflowDefinitionVersion>, ThrowingQueries<WorkflowDefinitionVersion>>();
        services.AddSingleton<IAddCommand<WorkflowDefinition>, StubAddCommand<WorkflowDefinition>>();
        services.AddSingleton<IAddCommand<WorkflowDefinitionVersion>, StubAddCommand<WorkflowDefinitionVersion>>();
        return services;
    }

    private sealed class TestWorkflowsDesignReconciliationFeature : WorkflowsDesignReconciliationFeature;

    private sealed class StubSource(string kind, string id) : IWorkflowReconciliationSource
    {
        public string SourceKind { get; } = kind;
        public string SourceId { get; } = id;
        public ValueTask<IEnumerable<Workflows.Design.Reconciliation.Models.WorkflowVersionReconciliationModel>> Read(CancellationToken cancellationToken)
            => ValueTask.FromResult<IEnumerable<Workflows.Design.Reconciliation.Models.WorkflowVersionReconciliationModel>>([]);
    }

    private sealed class StubIdentityGenerator : IIdentityGenerator
    {
        public string Generate() => Guid.NewGuid().ToString("N");
    }

    private sealed class StubDomainEventSender : IDomainEventSender
    {
        public Task Send(IDomainEvent domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubAddCommand<TEntity> : IAddCommand<TEntity> where TEntity : Entity
    {
        public Task Add(TEntity entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingQueries<TEntity> : IQueries<TEntity> where TEntity : Entity
    {
        private const string Msg = "Registration smoke test: query should not have been called.";
        public Task<TEntity?> Find(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<TEntity?> Find(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<TEntity?> Find<TProperty>(IFilter<TEntity> filter, System.Linq.Expressions.Expression<Func<TEntity, TProperty>> include, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<TEntity?> Find<TProperty>(IFilter<TEntity> filter, IEnumerable<System.Linq.Expressions.Expression<Func<TEntity, TProperty>>> include, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<TEntity?> Find(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> FindMany(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> FindMany(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> FindMany<TProp>(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, Elsa.Primitives.Persistence.OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<Elsa.Primitives.Persistence.Page<TEntity>> FindMany(System.Linq.Expressions.Expression<Func<TEntity, bool>>? predicate, Elsa.Primitives.Persistence.PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<Elsa.Primitives.Persistence.Page<TEntity>> FindMany(IFilter<TEntity> filter, Elsa.Primitives.Persistence.PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<Elsa.Primitives.Persistence.Page<TEntity>> FindMany<TProp>(System.Linq.Expressions.Expression<Func<TEntity, bool>>? predicate, Elsa.Primitives.Persistence.OrderDefinition<TEntity, TProp> order, Elsa.Primitives.Persistence.PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> List(CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> Query(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> Query(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> Query<TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Elsa.Primitives.Persistence.OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TResult>> Query<TResult>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, System.Linq.Expressions.Expression<Func<TEntity, TResult>> selector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TResult>> Query<TResult>(IFilter<TEntity> filter, System.Linq.Expressions.Expression<Func<TEntity, TResult>> selector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> Query<TProperty>(IFilter<TEntity> filter, Elsa.Primitives.Persistence.OrderDefinition<TEntity, TProperty> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TResult>> Query<TResult, TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, System.Linq.Expressions.Expression<Func<TEntity, TResult>> selector, Elsa.Primitives.Persistence.OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TResult>> Query<TResult, TProp>(IFilter<TEntity> filter, System.Linq.Expressions.Expression<Func<TEntity, TResult>> selector, Elsa.Primitives.Persistence.OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<long> Count(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<bool> Any(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<bool> Any(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<bool> Any(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<long> Count(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<long> Count(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<long> Count<TProperty>(IFilter<TEntity> filter, System.Linq.Expressions.Expression<Func<TEntity, TProperty>> propertySelector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<long> Count<TProperty>(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, System.Linq.Expressions.Expression<Func<TEntity, TProperty>> propertySelector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    }
}
