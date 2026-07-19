using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.DependencyInjection;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Persistence.Core.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Documents.Store;
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

    public static AspNetCoreIdentityGroundworkAcceptanceFixture Create()
    {
        var services = new ServiceCollection();
        var documents = new InMemoryDocumentStore(IdentityStorageManifest.Create());

        services.AddLogging();
        services.AddSingleton<IDocumentStore>(documents);
        services.AddSingleton<IBoundedDocumentStore>(documents);
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
