using Elsa.Persistence.EntityFramework;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Foundation.DataProtection.EntityFrameworkCore.Tests;

/// <summary>
/// The order a host starts its hosted services in, which is the order it resolves them in: the key store's migrator first,
/// then Data Protection's own key-ring loader, so the loader reads a table the migrator has made current (#2191). The key
/// store moves the loader there; this holds it there, and names the loader by the internal type the move matches, so a
/// framework release that renames it fails here rather than leaving the move a silent no-op.
/// </summary>
public sealed class KeyRingStartupOrderTests : IDisposable
{
    private const string KeyRingLoader = "Microsoft.AspNetCore.DataProtection.Internal.DataProtectionHostedService";
    private readonly SqliteKeyRingDatabase _database = new();

    public void Dispose() => _database.Dispose();

    /// <summary>
    /// Whether Data Protection was composed first by the host's own call or already by an earlier one, such as
    /// <c>AddAuthentication</c> or <c>AddAntiforgery</c>, which register the loader ahead of anything the host composes later.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_key_store_migrates_before_the_key_ring_loads(bool composedEarlier)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection((await _database.CreateStoreAsync()).Settings());
        if (composedEarlier)
            builder.Services.AddDataProtection();
        builder.Services.AddConfiguredDataProtection(builder.Configuration);
        using var host = builder.Build();

        var started = host.Services.GetServices<IHostedService>().Select(service => service.GetType()).ToList();

        var migrator = started.IndexOf(typeof(EfModuleMigrator<DataProtectionKeysDbContext>));
        var loader = started.FindIndex(type => type.FullName == KeyRingLoader);
        Assert.True(migrator >= 0, $"The key store's migrator is not among the host's hosted services: {Describe(started)}.");
        Assert.True(loader >= 0, $"Data Protection's key-ring loader, {KeyRingLoader}, is not among the host's hosted services: {Describe(started)}.");
        Assert.True(migrator < loader, $"Data Protection loads the key ring before the key store migrates: {Describe(started)}.");
        // Every hosted service of Data Protection's own, the loader among them, starts after the migrator.
        Assert.All(
            started.Select((type, index) => (Type: type, Index: index)).Where(service => service.Type.Assembly == typeof(KeyManagementOptions).Assembly),
            service => Assert.True(service.Index > migrator, $"{service.Type.FullName} starts before the key store migrates: {Describe(started)}."));
    }

    private static string Describe(IEnumerable<Type> started) => string.Join(", ", started.Select(type => type.Name));
}
