using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Targets;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Elsa.Persistence.Groundwork.RegistrationTests;

internal static class GroundworkProviderRegistrationAssertions
{
    /// <summary>
    /// Asserts that a provider preset declared exactly one target, named <see cref="GroundworkTargetNames.Default"/>,
    /// and wired its admission behind the shared driver. The document-store contracts are published twice on
    /// purpose: once under the target key and once unkeyed, because the default target is what a store that
    /// injects <see cref="IDocumentStore"/> without knowing about targets resolves.
    /// </summary>
    public static void AssertStartupLeafRegistration<TInitializer>(ServiceCollection services, string registrationSecret)
        where TInitializer : class, IGroundworkTargetAdmission
    {
        const string target = GroundworkTargetNames.Default;
        AssertPublishedForDefaultTarget<IDocumentStore>(services, target);
        AssertPublishedForDefaultTarget<IBoundedDocumentStore>(services, target);
        AssertPublishedForDefaultTarget<GroundworkStoreSessionSource>(services, target);
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(TInitializer) && IsKeyedFor(descriptor, target));

        var registry = Assert.Single(services
            .Where(descriptor => descriptor.ServiceType == typeof(GroundworkTargetRegistry))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<GroundworkTargetRegistry>());
        Assert.Equal([target], registry.TargetNames);

        // One admission driver is registered regardless of how many targets or providers are composed.
        var lifecycleRegistration = Assert.Single(
            services
                .Where(descriptor => descriptor.ServiceType == typeof(ShellInitializerRegistration))
                .Select(descriptor => descriptor.ImplementationInstance)
                .OfType<ShellInitializerRegistration>(),
            candidate => candidate.InitializerType == typeof(GroundworkTargetAdmissionInitializer));
        Assert.Equal(LifecyclePhase.Prepare, lifecycleRegistration.Phase);
        Assert.Equal(-1, lifecycleRegistration.RegistrationIndex);

        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
        try
        {
            var initializer = provider.GetRequiredKeyedService<TInitializer>(target);
            Assert.Equal(target, initializer.TargetName);

            var driver = provider.GetRequiredService<GroundworkTargetAdmissionInitializer>();
            Assert.Same(driver, Assert.Single(provider.GetServices<IHostedService>().OfType<GroundworkTargetAdmissionInitializer>()));
            Assert.Same(driver, Assert.Single(provider.GetServices<IShellInitializer>().OfType<GroundworkTargetAdmissionInitializer>()));
            Assert.Same(initializer, Assert.Single(provider.GetServices<IGroundworkTargetAdmission>().OfType<TInitializer>()));
            Assert.False(provider.GetRequiredService<GroundworkStoreSessionSource>().IsInitialized);

            using var scope = provider.CreateScope();
            Assert.IsType<GroundworkScopedDocumentStore>(scope.ServiceProvider.GetRequiredService<IDocumentStore>());
            Assert.IsType<GroundworkScopedDocumentStore>(scope.ServiceProvider.GetRequiredService<IBoundedDocumentStore>());
            Assert.Same(
                scope.ServiceProvider.GetRequiredService<IDocumentStore>(),
                scope.ServiceProvider.GetRequiredKeyedService<IDocumentStore>(target));
        }
        finally
        {
            provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void AssertPublishedForDefaultTarget<TService>(ServiceCollection services, string target)
    {
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(TService) && IsKeyedFor(descriptor, target));
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(TService) && !descriptor.IsKeyedService);
    }

    private static bool IsKeyedFor(ServiceDescriptor descriptor, string target) =>
        descriptor.IsKeyedService && Equals(descriptor.ServiceKey, target);

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
