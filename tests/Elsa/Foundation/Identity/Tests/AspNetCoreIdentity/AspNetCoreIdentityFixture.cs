using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;

/// <summary>
/// Shared setup for the EF Core-backed ASP.NET Core Identity tests: builds a DI container with the full
/// provider-backed identity substrate over a private SQLite database. Disposed via <see cref="IAsyncDisposable"/>
/// so each test class tears its database down.
/// </summary>
public sealed class AspNetCoreIdentityFixture : IAsyncDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"elsa-identity-{Guid.NewGuid():N}.db");

    public ServiceProvider Services { get; }

    public AspNetCoreIdentityFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
            new IdentityIamEntityFrameworkCoreOptions
            {
                Provider = "Sqlite",
                ConnectionString = $"Data Source={databasePath};Pooling=False"
            });

        Services = services.BuildServiceProvider();
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>().Database.EnsureCreated();
    }

    public AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            File.Delete(file);
    }
}
