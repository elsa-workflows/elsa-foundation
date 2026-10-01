using Elsa.Cluster.Readability;
using Elsa.ExtensionBuilder.Api.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.ExtensionBuilder.Api.Extensions;

/// <summary>
/// Root-container composition for the ExtensionBuilder subsystem. The subsystem is host-hosted (root
/// singletons, a background build worker, and management endpoints mapped on the root
/// <see cref="Microsoft.AspNetCore.Routing.IEndpointRouteBuilder"/> before <c>MapShells()</c>), so its
/// services must live in the root container rather than a shell scope. It is not a CShells shell feature:
/// a host that wants it calls this once at startup. No host composes it by default; the Workbench gates the
/// call behind the <c>Elsa:ExtensionBuilder:Enabled</c> config switch, which is off unless set.
/// </summary>
public static class ExtensionBuilderServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ExtensionBuilder options, stores, build queue/worker and services on the root container.
    /// </summary>
    public static IServiceCollection AddElsaExtensionBuilder(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExtensionBuilderOptions>(configuration.GetSection("Elsa:ExtensionBuilder"));
        services.AddPermissionContributor<ExtensionBuilderPermissionContributor>();
        services.AddSingleton<IExtensionBuilderTemplateCatalog, ExtensionBuilderTemplateCatalog>();
        services.AddSingleton<IExtensionBuilderStorage, ExtensionBuilderStorage>();
        services.AddSingleton<ExtensionBuilderBackgroundBuildQueue>();
        services.AddSingleton<IExtensionBuilderBuildQueue>(sp => sp.GetRequiredService<ExtensionBuilderBackgroundBuildQueue>());
        services.AddHostedService<ExtensionBuilderBuildWorker>();
        // The routes are mapped on the host, but the path-less shell resolves each request, and CShells copies every root
        // registration into every shell. A shell's own copy of the queue is read by no worker, so a build enqueued on it
        // would stay queued for ever, and a second storage would write state.json under a gate of its own (#2159 is the
        // same fault in Nuplane's trigger queue). Shells resolve the host's instances instead; IExtensionBuilderBuildQueue
        // follows, because its factory resolves the shared queue.
        services
            .ShareWithShells<IExtensionBuilderTemplateCatalog>()
            .ShareWithShells<IExtensionBuilderStorage>()
            .ShareWithShells<ExtensionBuilderBackgroundBuildQueue>();
        services.AddScoped<IExtensionBuilderBuildRunner, ExtensionBuilderBuildRunner>();
        services.AddScoped<IExtensionBuilderBuildExecutor, ExtensionBuilderBuildExecutor>();
        services.AddScoped<IExtensionBuilderPromotionService, ExtensionBuilderPromotionService>();
        services.AddScoped<IExtensionBuilderService, ExtensionBuilderService>();
        return services;
    }
}
