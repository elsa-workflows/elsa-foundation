using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.DependencyInjection;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Extensions;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;

internal sealed class AspNetCoreIdentityGroundworkAcceptanceFixture : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private AspNetCoreIdentityGroundworkAcceptanceFixture(
        ServiceProvider services,
        IReadOnlyCollection<ServiceDescriptor> serviceDescriptors)
    {
        _services = services;
        ServiceDescriptors = serviceDescriptors;
    }

    public IReadOnlyCollection<ServiceDescriptor> ServiceDescriptors { get; }

    public static AspNetCoreIdentityGroundworkAcceptanceFixture Create(
        IStorageProviderConnection? providerConnection = null)
    {
        var services = new ServiceCollection();
        var persistence = providerConnection is null
            ? new AspNetCoreIdentityTestPersistence()
            : new AspNetCoreIdentityTestPersistence(
                new NonDisposingStorageProviderConnection(providerConnection));

        services.AddLogging();
        services.AddSingleton<IStorageProviderConnection>(persistence.Connection);
        services.AddPersistenceCore(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);
        services.AddFoundationAspNetCoreIdentityGroundwork();

        return new AspNetCoreIdentityGroundworkAcceptanceFixture(
            services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            }),
            services.ToArray());
    }

    public AsyncServiceScope CreateScope() => _services.CreateAsyncScope();

    public async ValueTask DisposeAsync() => await _services.DisposeAsync();
}
