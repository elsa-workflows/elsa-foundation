using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Tests;

public sealed class StructuredLogsEntityFrameworkCoreFixture : IAsyncDisposable
{
    private readonly string directory = Path.Join(Path.GetTempPath(), "elsa-structured-logs-" + Guid.NewGuid().ToString("N"));
    private ServiceProvider? provider;

    public string DatabasePath => Path.Join(directory, "structured-logs.db");
    public StructuredLogStoreBinding Binding { get; } = new("tenant-a", "scope-a", "stream-a");
    public EfStructuredLogStore Store => provider!.GetRequiredService<EfStructuredLogStore>();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(directory);
        provider = BuildProvider(DatabasePath, Binding);
        await EnsureCreatedAsync(provider);
        Store.Start();
    }

    public static ServiceProvider BuildProvider(string path, StructuredLogStoreBinding? binding = null)
    {
        var services = new ServiceCollection();
        services.AddOptions<StructuredLogsOptions>();
        if (binding is not null)
            services.AddSingleton(binding);
        services.AddStructuredLogsEntityFrameworkCore(new StructuredLogsEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = $"Data Source={path}"
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public static async Task EnsureCreatedAsync(ServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<StructuredLogsDbContext>().Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (provider is not null)
            await provider.DisposeAsync();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
