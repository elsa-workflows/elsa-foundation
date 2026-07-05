using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;

/// <summary>
/// Shared setup for the EF-backed ASP.NET Core Identity tests: builds a DI container with the full
/// EF Core identity substrate over a private, uniquely-named in-memory database and ensures the schema
/// exists. Disposed via <see cref="IAsyncDisposable"/> so each test class tears its provider down.
/// </summary>
public sealed class AspNetCoreIdentityFixture : IAsyncDisposable
{
    public ServiceProvider Services { get; }

    public AspNetCoreIdentityFixture()
    {
        var databaseName = $"identity-{Guid.NewGuid():n}";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
            isDevelopmentOrDemo: false,
            configureDbContext: builder => builder.UseInMemoryDatabase(databaseName));

        Services = services.BuildServiceProvider();

        // The in-memory provider has no migrations; create the model so the stores can read/write.
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>().Database.EnsureCreated();
    }

    public AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    public async ValueTask DisposeAsync() => await Services.DisposeAsync();
}
