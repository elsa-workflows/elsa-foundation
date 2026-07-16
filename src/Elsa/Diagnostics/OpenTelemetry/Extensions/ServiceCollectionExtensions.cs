using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Providers.InMemory;
using Elsa.Diagnostics.OpenTelemetry.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Diagnostics.OpenTelemetry.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the OpenTelemetry diagnostics pipeline as a set of independently overridable contracts:
    /// source registry, redactor, in-memory store, in-memory live feed, ingestor, provider, and collector
    /// configuration provider. Each is registered with <c>TryAdd*</c> so a persistence or transport feature
    /// can replace a single role without touching the rest.
    /// </summary>
    public static IServiceCollection AddOpenTelemetryDiagnosticsServices(this IServiceCollection services, Action<OpenTelemetryDiagnosticsOptions>? configureOptions = null)
    {
        if (configureOptions != null)
            services.Configure(configureOptions);

        services.AddOptions<OpenTelemetryDiagnosticsOptions>();
        services.TryAddSingleton<IOpenTelemetrySourceRegistry, OpenTelemetrySourceRegistry>();
        services.TryAddSingleton<IOpenTelemetryRedactor, OpenTelemetryRedactor>();
        services.TryAddSingleton<InMemoryOpenTelemetryStore>();
        services.TryAddSingleton<IOpenTelemetryStore>(sp => sp.GetRequiredService<InMemoryOpenTelemetryStore>());
        services.TryAddSingleton<InMemoryOpenTelemetryLiveFeed>();
        services.TryAddSingleton<IOpenTelemetryLiveFeed>(sp => sp.GetRequiredService<InMemoryOpenTelemetryLiveFeed>());
        services.TryAddSingleton<IOpenTelemetryIngestor, OpenTelemetryIngestor>();
        services.TryAddSingleton<IOpenTelemetryProvider, DefaultOpenTelemetryProvider>();
        services.TryAddSingleton<ICollectorConfigurationProvider, CollectorConfigurationProvider>();

        return services;
    }

    /// <summary>
    /// Adds a singleton contributor to the post-redaction ingestion pipeline without replacing contributors
    /// registered by other features. Re-registering the same implementation type is idempotent.
    /// </summary>
    public static IServiceCollection AddOpenTelemetryIngestionContributor<TContributor>(this IServiceCollection services)
        where TContributor : class, IOpenTelemetryIngestionContributor
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOpenTelemetryIngestionContributor, TContributor>());
        return services;
    }
}
