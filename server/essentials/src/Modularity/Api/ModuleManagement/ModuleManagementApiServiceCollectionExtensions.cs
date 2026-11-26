using System.Text.Json;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;
using Elsa.Modularity.Nuplane.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Modularity.Api.ModuleManagement;

public static class ModuleManagementApiServiceCollectionExtensions
{
    public static IServiceCollection AddElsaModuleManagement(
        this IServiceCollection services,
        Action<ModuleManagementApiOptions>? configure = null)
    {
        services.AddOptions<ModuleManagementApiOptions>();
        if (configure is not null)
            services.Configure(configure);

        services.AddNuplaneFeatureCatalog();
        services.TryAddScoped<IModuleRegistryService, ModuleRegistryService>();
        services.TryAddScoped<IShellFeatureConfigurationStore, NullShellFeatureConfigurationStore>();
        services.TryAddScoped<IShellReloader, NullShellReloader>();
        return services;
    }
}

internal sealed class NullShellFeatureConfigurationStore : IShellFeatureConfigurationStore
{
    private static readonly ShellFeatureConfigurationSnapshot Empty = new("host", "", new Dictionary<string, JsonElement>());

    public Task<ShellFeatureConfigurationSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Empty);

    public Task<ShellFeatureConfigurationSnapshot> SaveAsync(
        string expectedRevision,
        IReadOnlyList<FeatureConfigurationChange> features,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Shell feature configuration is not available at the host level.");
}

internal sealed class NullShellReloader : IShellReloader
{
    public Task<int> ReloadAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
}