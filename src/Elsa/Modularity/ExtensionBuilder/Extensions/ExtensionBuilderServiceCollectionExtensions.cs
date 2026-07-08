using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Modularity.ExtensionBuilder.Extensions;

/// <summary>
/// Root-container composition for the ExtensionBuilder subsystem. The subsystem is host-hosted (root
/// singletons, a background build worker, and management endpoints mapped on the root
/// <see cref="Microsoft.AspNetCore.Routing.IEndpointRouteBuilder"/> before <c>MapShells()</c>), so its
/// services must live in the root container rather than a shell scope. It is not a CShells shell feature:
/// the host calls this once at startup, gated by the <c>Elsa:ExtensionBuilder:Enabled</c> config switch.
/// </summary>
public static class ExtensionBuilderServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ExtensionBuilder options, stores, build queue/worker and services on the root container.
    /// </summary>
    public static IServiceCollection AddElsaExtensionBuilder(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExtensionBuilderOptions>(configuration.GetSection("Elsa:ExtensionBuilder"));
        services.AddSingleton<IExtensionBuilderTemplateCatalog, ExtensionBuilderTemplateCatalog>();
        services.AddSingleton<IExtensionBuilderStorage, ExtensionBuilderStorage>();
        services.AddSingleton<ExtensionBuilderBackgroundBuildQueue>();
        services.AddSingleton<IExtensionBuilderBuildQueue>(sp => sp.GetRequiredService<ExtensionBuilderBackgroundBuildQueue>());
        services.AddHostedService<ExtensionBuilderBuildWorker>();
        services.AddScoped<IExtensionBuilderBuildRunner, ExtensionBuilderBuildRunner>();
        services.AddScoped<IExtensionBuilderBuildExecutor, ExtensionBuilderBuildExecutor>();
        services.AddScoped<IExtensionBuilderPromotionService, ExtensionBuilderPromotionService>();
        services.AddScoped<IExtensionBuilderService, ExtensionBuilderService>();
        return services;
    }
}
