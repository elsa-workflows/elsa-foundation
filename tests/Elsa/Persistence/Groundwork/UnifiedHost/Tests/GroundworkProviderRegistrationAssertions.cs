using CShells.Lifecycle;
using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Foundation.Identity.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Secrets.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Elsa.Persistence.Groundwork.RegistrationTests;

internal static class GroundworkProviderRegistrationAssertions
{
    public static void AssertStartupLeafRegistration<TInitializer>(ServiceCollection services, string registrationSecret)
        where TInitializer : class, IHostedService, IShellInitializer
    {
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IDocumentStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IBoundedDocumentStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkDocumentStoreHolder));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TInitializer));

        var provider = services.BuildServiceProvider();
        try
        {
            var initializer = provider.GetRequiredService<TInitializer>();

            Assert.Same(initializer, Assert.Single(provider.GetServices<IHostedService>().OfType<TInitializer>()));
            Assert.Same(initializer, Assert.Single(provider.GetServices<IShellInitializer>().OfType<TInitializer>()));
            Assert.NotNull(provider.GetRequiredService<GroundworkDocumentStoreHolder>());

            var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IDocumentStore>());
            Assert.Contains("not been initialized", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(registrationSecret, exception.ToString(), StringComparison.Ordinal);
            var boundedException = Assert.Throws<InvalidOperationException>(() =>
                provider.GetRequiredService<IBoundedDocumentStore>());
            Assert.Contains("not been initialized", boundedException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(registrationSecret, boundedException.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public static void AssertRepresentativeFamilyContracts(ServiceCollection services, params Type[] contracts)
    {
        foreach (var contract in contracts)
            Assert.Contains(services, descriptor => descriptor.ServiceType == contract);
    }

    public static void AssertRegistrationDiagnosticsAreSanitized(
        ServiceCollection services,
        string registrationSecret,
        string connectionString)
    {
        var diagnostics = string.Join(Environment.NewLine, services.Select(descriptor => descriptor.ToString()));
        Assert.DoesNotContain(registrationSecret, diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, diagnostics, StringComparison.Ordinal);
    }

    public static void SelectAllGroundworkFamilies(IServiceCollection services)
    {
        services.AddGroundworkRuntimeStores();
        services.AddGroundworkIdentityStores();
        services.AddGroundworkSecretsStore();
        services.AddGroundworkDistributedRuntimeStores();
        services.AddGroundworkWorkflowsDesignStores();
        services.AddGroundworkActivitiesDesignStores();
        services.AddGroundworkPublishingStores();
    }
}
