using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Tests;

public sealed class PublishingGroundworkLifetimeTests
{
    [Fact]
    public async Task Groundwork_consumers_are_scoped_and_do_not_cross_request_scopes()
    {
        await using var persistence = await PublishingV2TestPersistence.CreateAsync("memory");
        var services = new ServiceCollection();
        // The auth-free engine services (runtime coordinator/activator, snapshot review, policy resolver,
        // preflight, and publication stores) are registered by the two publishing features. This test
        // composes both directly, as the shell's feature dependency does.
        new Elsa.Workflows.Publishing.WorkflowsPublishingFeature().ConfigureServices(services);
        new WorkflowsPublishingApiFeature().ConfigureServices(services);
        services.AddGroundworkPublishingStores();
        services.AddSingleton<IGroundworkStorageSessionSource>(persistence.Sessions);
        services.AddSingleton<IPersistenceAccessContextAccessor>(persistence.Access());
        services.RemoveAll<IWorkflowExecutableStore>();
        services.AddScoped<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        services.AddScoped<IWorkflowExecutableRootWriteLeaseManager, WorkflowExecutableRootWriteLeaseManager>();
        services.AddScoped<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();
        services.AddScoped<IWorkflowTriggerIndexer, NoopWorkflowTriggerIndexer>();

        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, x => x.ServiceType == typeof(IWorkflowActivationAuthority)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, x => x.ServiceType == typeof(IWorkflowActivationCoordinator)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, x => x.ServiceType == typeof(IPublicationActivator)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, x => x.ServiceType == typeof(PublicationSnapshotReviewService)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, x => x.ServiceType == typeof(IPublicationPolicyResolver)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, x => x.ServiceType == typeof(IPublicationPreflightService)).Lifetime);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        AssertScopedAcrossRequests<IWorkflowActivationCoordinator>(firstScope, secondScope);
        AssertScopedAcrossRequests<IPublicationActivator>(firstScope, secondScope);
        AssertScopedAcrossRequests<PublicationSnapshotReviewService>(firstScope, secondScope);
        AssertScopedAcrossRequests<IPublicationRecordStore>(firstScope, secondScope);
        AssertScopedAcrossRequests<IPublicationPolicyStore>(firstScope, secondScope);
        AssertScopedAcrossRequests<IPublicationProjectionIntentStore>(firstScope, secondScope);
        AssertScopedAcrossRequests<IPublicationSnapshotReviewStore>(firstScope, secondScope);
        AssertScopedAcrossRequests<IActivityPublicationReceiptStore>(firstScope, secondScope);
        AssertScopedAcrossRequests<IActivityDraftTestRunStore>(firstScope, secondScope);

        Assert.Same(
            provider.GetRequiredService<IPublicationPolicyResolver>(),
            firstScope.ServiceProvider.GetRequiredService<IPublicationPolicyResolver>());
        Assert.Same(
            provider.GetRequiredService<IPublicationPreflightService>(),
            secondScope.ServiceProvider.GetRequiredService<IPublicationPreflightService>());
    }

    private static void AssertScopedAcrossRequests<TService>(IServiceScope firstScope, IServiceScope secondScope)
        where TService : class
    {
        var first = firstScope.ServiceProvider.GetRequiredService<TService>();
        Assert.Same(first, firstScope.ServiceProvider.GetRequiredService<TService>());
        Assert.NotSame(first, secondScope.ServiceProvider.GetRequiredService<TService>());
    }

    private sealed class NoopWorkflowTriggerIndexer : IWorkflowTriggerIndexer
    {
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(
            WorkflowExecutable executable,
            CancellationToken cancellationToken = default) =>
            new((IReadOnlyCollection<WorkflowTriggerBinding>)[]);
    }
}
