using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Serialization.Testing;

/// <summary>
/// Serialization services as the public <see cref="SerializationFeature"/> composes them, for tests in any domain. Each
/// call composes a fresh set; no startup task runs, so no converter is contributed unless the caller supplies a registry.
/// </summary>
public static class SerializationServices
{
    /// <summary>A payload serializer over <paramref name="converters"/>, or over an empty converter registry.</summary>
    public static IPayloadSerializer PayloadSerializer(JsonPayloadConverterRegistry? converters = null) =>
        Compose(converters).GetRequiredService<IPayloadSerializer>();

    /// <summary>An empty well-known type registry.</summary>
    public static IWellKnownTypeRegistry TypeRegistry() => Compose().GetRequiredService<IWellKnownTypeRegistry>();

    /// <summary>An object converter with no contributed type converters.</summary>
    public static IObjectConverter ObjectConverter() => Compose().GetRequiredService<IObjectConverter>();

    private static ServiceProvider Compose(JsonPayloadConverterRegistry? converters = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        new SerializationFeature().ConfigureServices(services);

        // Registered after the feature's own registry, so it is the one the serializer resolves.
        if (converters is not null)
            services.AddSingleton(converters);

        return services.BuildServiceProvider();
    }
}
