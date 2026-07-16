using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Tests;

public sealed class PublishingGroundworkLifetimeTests
{
    [Fact]
    public void Groundwork_consumers_are_scoped_and_do_not_cross_request_scopes()
    {
        var services = new ServiceCollection();
        new WorkflowsPublishingApiFeature().ConfigureServices(services);
        services.AddGroundworkPublishingStores();
        services.AddScoped<IDocumentStore>(_ => new InMemoryDocumentStore(PublishingGroundworkStorageManifest.Create()));
        services.AddScoped<IBoundedDocumentStore>(sp =>
            new PublishingTestBoundedDocumentStore(sp.GetRequiredService<IDocumentStore>()));
        services.RemoveAll<IWorkflowExecutableStore>();
        services.AddScoped<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        services.AddScoped<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();
        services.AddScoped<IWorkflowTriggerIndexer, NoopWorkflowTriggerIndexer>();

        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, x => x.ServiceType == typeof(IPublicationProjectionPreparer)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, x => x.ServiceType == typeof(IPublicationActivator)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, x => x.ServiceType == typeof(PublicationSnapshotReviewService)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, x => x.ServiceType == typeof(IPublicationPolicyResolver)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, x => x.ServiceType == typeof(IPublicationPreflightService)).Lifetime);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        AssertScopedAcrossRequests<IPublicationProjectionPreparer>(firstScope, secondScope);
        AssertScopedAcrossRequests<IPublicationActivator>(firstScope, secondScope);
        AssertScopedAcrossRequests<PublicationSnapshotReviewService>(firstScope, secondScope);
        AssertScopedAcrossRequests<IPublicationSlotStore>(firstScope, secondScope);
        AssertScopedAcrossRequests<IPublicationRecordStore>(firstScope, secondScope);
        AssertScopedAcrossRequests<IPublicationPolicyStore>(firstScope, secondScope);
        AssertScopedAcrossRequests<IPublicationProjectionIntentStore>(firstScope, secondScope);
        AssertScopedAcrossRequests<IPublicationSnapshotReviewStore>(firstScope, secondScope);

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
