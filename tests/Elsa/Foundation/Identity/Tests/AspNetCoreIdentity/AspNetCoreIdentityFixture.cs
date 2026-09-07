using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.DependencyInjection;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;

/// <summary>
/// Shared setup for the Groundwork-backed ASP.NET Core Identity tests: builds a DI container with the full
/// provider-backed identity substrate over a private SQLite database. Disposed via <see cref="IAsyncDisposable"/>
/// so each test class tears its provider down.
/// </summary>
public sealed class AspNetCoreIdentityFixture : IAsyncDisposable
{
    public ServiceProvider Services { get; }

    public AspNetCoreIdentityFixture()
    {
        var persistence = new IdentityV2TestPersistence();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(persistence);
        services.AddSingleton<IStorageProviderConnection>(p => p.GetRequiredService<IdentityV2TestPersistence>().Connection);
        services.AddPersistenceCore();
        services.AddFoundationAspNetCoreIdentityGroundwork();

        Services = services.BuildServiceProvider();
    }

    public AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    public async ValueTask DisposeAsync() => await Services.DisposeAsync();
}
