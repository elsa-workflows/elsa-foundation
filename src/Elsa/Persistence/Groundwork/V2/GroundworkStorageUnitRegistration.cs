using CShells.Lifecycle;
using Groundwork.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Persistence.Groundwork.Composition;

public static class GroundworkStorageUnitServiceCollectionExtensions
{
    public static IServiceCollection AddGroundworkStorageUnit(
        this IServiceCollection services,
        StorageUnit unit,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(unit);

        var registry = FindOrAddRegistry(services);
        registry.Declare(unit, targetName);
        AddRuntimeOnce(services);
        return services;
    }

    private static GroundworkStorageUnitRegistry FindOrAddRegistry(IServiceCollection services)
    {
        var existing = services
            .Where(descriptor => descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<GroundworkStorageUnitRegistry>()
            .SingleOrDefault();
        if (existing is not null)
            return existing;

        var created = new GroundworkStorageUnitRegistry();
        services.AddSingleton(created);
        return created;
    }

    private static void AddRuntimeOnce(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(GroundworkStorageSessionSource)))
            return;

        services.AddSingleton<GroundworkStorageSessionSource>();
        services.AddSingleton<IGroundworkStorageSessionSource>(provider =>
            provider.GetRequiredService<GroundworkStorageSessionSource>());
        services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<GroundworkStorageSessionSource>());
        services.AddSingleton<IShellInitializer>(provider =>
            provider.GetRequiredService<GroundworkStorageSessionSource>());
    }
}
