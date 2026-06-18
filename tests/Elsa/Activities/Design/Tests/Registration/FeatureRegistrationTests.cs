using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EFCore.Sqlite;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Clr;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Persistence.EFCore.Events;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Hosting.Services;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
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

        Assert.NotNull(provider.GetService<IActivityConstructorRegistry>());

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<IActivityFactory>());

        // The constructor registry is populated via the Registry + StartUp Task + Domain Event
        // pattern (framework §2.6.1): the startup task publishes the init event; the single
        // aggregating handler adds every contributed IActivityConstructor.
        Assert.Contains(services, d => d.ServiceType == typeof(IStartupTask)
            && d.ImplementationType == typeof(Elsa.Activities.Runtime.Tasks.ActivityConstructorsStartupTask));
        Assert.Contains(services, d => d.ServiceType == typeof(IEventHandler)
            && d.ImplementationType == typeof(Elsa.Activities.Runtime.Handlers.RegisterActivityConstructors));
    }

    [Fact]
    public void ActivitiesDesignReconciliationFeature_RegistersReconcilerHasherStartupTaskAndHandler()
    {
        var services = MinimalServices();
        StubReconcilerDependencies(services);

        // The reshaped feature (FR-021) is non-abstract and owns no sources — the universal
        // handler resolves IEnumerable<IActivityReconciliationSource> from DI, which is empty
        // here because no source feature is composed alongside it.
        new ActivitiesDesignReconciliationFeature().ConfigureServices(services);

        // The single startup task that drives a reconciliation pass is wired (asserted on the
        // descriptor — its IDistributedLockProvider dependency is supplied by the host, not stubbed).
        Assert.Contains(services, d => d.ServiceType == typeof(IStartupTask)
            && d.ImplementationType == typeof(Elsa.Activities.Design.Reconciliation.Services.ActivityVersionReconcilerStartupTask));

        // The universal reconciling handler is wired (registered as the non-generic IEventHandler
        // service; the event pipeline filters by the closed IEventHandler<TEvent> interface).
        Assert.Contains(services, d => d.ServiceType == typeof(IEventHandler)
            && d.ImplementationType == typeof(Elsa.Activities.Design.Reconciliation.Handlers.CollectActivityVersions));

        using var provider = services.BuildServiceProvider();

        // The content hasher + entity factories are registered by the persistence feature now, not the
        // reconciliation feature; this feature only wires the reconciler, its startup task, and handler.
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<IActivityVersionReconciler>());
    }

    [Fact]
    public void ClrActivityReconciliationFeature_RegistersClrReconciliationSource()
    {
        var services = MinimalServices();

        new ClrActivityReconciliationFeature
        {
            Options = { FolderPath = Path.Combine(Path.GetTempPath(), "activities") }
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        // The standalone source feature (§2.6.1) contributes exactly the CLR source; the
        // reconciliation feature's universal handler discovers it from this same DI surface.
        var source = provider.GetService<IActivityReconciliationSource>();
        Assert.IsType<ClrActivityReconciliationSource>(source);
        Assert.Equal("CLR", source.SourceKind);
    }

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

        // The migrated saving handler is a typed IEntitySavingHandler<,> contributor (the assembly
        // scan registers it); the single ApplyEntitySavingHandlers aggregator — registered once by
        // the EF Core base feature — is the sole IEventHandler<OnEntitySaving> that dispatches it.
        Assert.NotNull(scope.ServiceProvider.GetService<Elsa.Persistence.EFCore.Contracts.IEntitySavingHandler<
            Elsa.Activities.Design.Persistence.EFCore.DbContext.ActivitiesDesignDbContext,
            ActivityDefinitionVersion>>());

        var savingSubscribers = scope.ServiceProvider
            .GetServices<IEventHandler>()
            .OfType<IEventHandler<OnEntitySaving>>()
            .ToList();
        Assert.Single(savingSubscribers);
        Assert.IsType<Elsa.Persistence.EFCore.Handlers.ApplyEntitySavingHandlers>(savingSubscribers[0]);
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
        services.AddSingleton<IActivityDefinitionHasher, StubActivityDefinitionHasher>();
        services.AddSingleton<IActivityDefinitionStore, Integration.ThrowingActivityDefinitionStore>();
        services.AddSingleton<IActivityDefinitionVersionStore, Integration.ThrowingActivityDefinitionVersionStore>();
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

    private sealed class StubActivityDefinitionHasher : IActivityDefinitionHasher
    {
        public string Hash(IActivityDefinition definition, IActivityDefinitionVersion version) => "stub";
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
