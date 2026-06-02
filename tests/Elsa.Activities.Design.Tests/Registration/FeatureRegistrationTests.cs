using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.Sqlite;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Resolvers;
using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Persistence.EFCore.Events;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Services;
using Elsa.Serialization.Core;
using Elsa.Serialization.Services;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Activities.Design.Tests.Registration;

/// <summary>
/// §2.23.1 registration tests for the four new Unit B feature classes. Each test composes
/// the feature into a real <see cref="ServiceCollection"/>, pre-registers the minimal set
/// of cross-feature prerequisites, builds the provider, and asserts the feature's claimed
/// services resolve cleanly. Catches DI misconfiguration before runtime.
/// </summary>
public sealed class FeatureRegistrationTests
{
    [Fact]
    public void ActivitiesRuntimeFeature_RegistersFactoryAndRegistries()
    {
        var services = MinimalServices();
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IImplementationDescriptorRegistry>());
        Assert.NotNull(provider.GetService<IActivityImplementationResolverRegistry>());
        Assert.NotNull(provider.GetService<ClrActivityImplementationResolver>());

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<IActivityFactory>());

        // Two startup tasks (resolver registry + descriptor registry).
        var startupTasks = scope.ServiceProvider.GetServices<IStartupTask>().ToList();
        Assert.True(startupTasks.Count >= 2);

        // Handlers register as non-generic IEventHandler (the event pipeline
        // filters by interface). Two contributors expected: CLR resolver + CLR descriptor type.
        var allHandlers = scope.ServiceProvider.GetServices<IEventHandler>().ToList();
        Assert.Contains(allHandlers, h => h is IEventHandler<Elsa.Activities.Runtime.Core.Events.OnActivityImplementationResolversInitializing>);
        Assert.Contains(allHandlers, h => h is IEventHandler<Elsa.Activities.Design.Core.Events.OnImplementationDescriptorsInitializing>);
    }

    [Fact]
    public void ActivitiesDesignReconciliationFeature_RegistersReconcilerAndHasher()
    {
        var services = MinimalServices();
        StubReconcilerDependencies(services);
        new TestActivitiesDesignReconciliationFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        // Hasher is a singleton and must resolve directly.
        Assert.NotNull(provider.GetService<IActivityDefinitionHasher>());

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<IActivityVersionReconciler>());
    }

    private sealed class TestActivitiesDesignReconciliationFeature : ActivitiesDesignReconciliationFeature;

    [Fact]
    public void SqliteActivitiesDesignPersistenceShellFeature_RegistersLookupAndSavingHandler()
    {
        var services = MinimalServices();
        services.AddSingleton<JsonPayloadConverterRegistry>();
        services.AddSingleton<IPayloadSerializer, JsonPayloadSerializer>();

        new SqliteActivitiesDesignPersistenceShellFeature
        {
            ConnectionString = "Data Source=:memory:"
        }.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();

        // Lookup service resolves (UseQueries path).
        Assert.NotNull(scope.ServiceProvider.GetService<IActivityDefinitionLookup>());

        // Add-command for activity definitions resolves (UseCommands path).
        Assert.NotNull(scope.ServiceProvider.GetService<IAddActivityDefinitionCommand>());

        // IQueries<> for each entity resolves via ConfigureQueries<TDbContext>.
        Assert.NotNull(scope.ServiceProvider.GetService<IQueries<ActivityDefinition>>());
        Assert.NotNull(scope.ServiceProvider.GetService<IQueries<ActivityDefinitionVersion>>());

        // The migrated saving handler resolves through the OnEntitySaving event
        // surface (Unit A code-checklist closure). Handlers register as non-generic.
        var allHandlers = scope.ServiceProvider.GetServices<IEventHandler>().ToList();
        Assert.Contains(allHandlers, h => h is Elsa.Activities.Design.Persistence.EFCore.EntityHandlers.ActivityDefinitionVersionSavingHandler);
    }

    /// <summary>
    /// Foundational services the host normally registers before any feature runs.
    /// </summary>
    private static ServiceCollection MinimalServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<IEventPublisher, StubEventPublisher>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddLogging();
        return services;
    }

    /// <summary>
    /// Reconciler depends on a handful of cross-feature persistence services. Stub them so
    /// the registration test focuses on whether the Reconciliation feature itself wires
    /// correctly — not on a full persistence stack. Under Model X (Unit C 2026-05-28) the
    /// reconciler no longer takes a clock, no longer queries / saves a reconciliation-state
    /// sibling — those dependencies have been removed.
    /// </summary>
    private static void StubReconcilerDependencies(IServiceCollection services)
    {
        services.AddSingleton<IIdentityGenerator, StubIdentityGenerator>();
        services.AddSingleton<IQueries<ActivityDefinition>, ThrowingQueriesForRegistration<ActivityDefinition>>();
        services.AddSingleton<IQueries<ActivityDefinitionVersion>, ThrowingQueriesForRegistration<ActivityDefinitionVersion>>();
        services.AddSingleton<IAddActivityDefinitionCommand, StubAddActivityDefinitionCommand>();
        services.AddSingleton<IAddCommand<ActivityDefinitionVersion>, StubAddCommand<ActivityDefinitionVersion>>();
    }

    private sealed class StubEventPublisher : IEventPublisher
    {
        public Task Publish(IEvent @event, IEventPublishingStrategy? strategy = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubIdentityGenerator : IIdentityGenerator
    {
        public string Generate() => Guid.NewGuid().ToString("N");
    }

    private sealed class ThrowingQueriesForRegistration<TEntity> : IQueries<TEntity> where TEntity : Elsa.Primitives.Entities.Entity
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

    private sealed class StubAddActivityDefinitionCommand : IAddActivityDefinitionCommand
    {
        public Task Execute(ActivityDefinition workflowDefinition, ActivityDefinitionVersion version, CancellationToken cancellation) => Task.CompletedTask;
    }

    private sealed class StubAddCommand<TEntity> : IAddCommand<TEntity> where TEntity : Elsa.Primitives.Entities.Entity
    {
        public Task Add(TEntity entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
