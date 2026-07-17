using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Stores;
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
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkStoreSessionSource));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TInitializer));
        var lifecycleRegistration = Assert.Single(
            services
                .Where(descriptor => descriptor.ServiceType == typeof(ShellInitializerRegistration))
                .Select(descriptor => descriptor.ImplementationInstance)
                .OfType<ShellInitializerRegistration>(),
            candidate => candidate.InitializerType == typeof(TInitializer));
        Assert.Equal(LifecyclePhase.Prepare, lifecycleRegistration.Phase);
        Assert.Equal(-1, lifecycleRegistration.RegistrationIndex);

        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
        try
        {
            var initializer = provider.GetRequiredService<TInitializer>();

            Assert.Same(initializer, Assert.Single(provider.GetServices<IHostedService>().OfType<TInitializer>()));
            Assert.Same(initializer, Assert.Single(provider.GetServices<IShellInitializer>().OfType<TInitializer>()));
            Assert.False(provider.GetRequiredService<GroundworkStoreSessionSource>().IsInitialized);

            using var scope = provider.CreateScope();
            Assert.IsType<GroundworkScopedDocumentStore>(scope.ServiceProvider.GetRequiredService<IDocumentStore>());
            Assert.IsType<GroundworkScopedDocumentStore>(scope.ServiceProvider.GetRequiredService<IBoundedDocumentStore>());
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
}
