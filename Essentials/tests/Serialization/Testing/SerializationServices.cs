using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Serialization.Testing;

/// <summary>
/// Serialization services as the public <see cref="SerializationFeature"/> composes them, for tests in any domain. No
/// startup task runs, so no converter is contributed unless the caller supplies a registry or runs the startup itself.
/// </summary>
/// <remarks>
/// Each shortcut composes its own container and hands out one service from it without disposing the container, because
/// the service often sits in a static field or outlives the helper that asked for it. That is sound only while the
/// composition holds nothing a container would dispose: then an undisposed container is managed memory, collected
/// once nothing references it. <c>SerializationServicesTests</c> fails the day that stops being true. A caller with a
/// lifecycle of its own composes with <see cref="Compose"/> and owns the provider it builds.
/// </remarks>
public static class SerializationServices
{
    /// <summary>A payload serializer over <paramref name="converters"/>, or over an empty converter registry.</summary>
    public static IPayloadSerializer PayloadSerializer(JsonPayloadConverterRegistry? converters = null) =>
        Compose(converters).BuildServiceProvider().GetRequiredService<IPayloadSerializer>();

    /// <summary>An empty well-known type registry.</summary>
    public static IWellKnownTypeRegistry TypeRegistry() =>
        Compose().BuildServiceProvider().GetRequiredService<IWellKnownTypeRegistry>();

    /// <summary>An object converter with no contributed type converters.</summary>
    public static IObjectConverter ObjectConverter() =>
        Compose().BuildServiceProvider().GetRequiredService<IObjectConverter>();

    /// <summary>
    /// The services of <see cref="SerializationFeature"/>, logging to nowhere, over <paramref name="converters"/> when
    /// given. Callers add their own services before building, and own the provider they build.
    /// </summary>
    public static ServiceCollection Compose(JsonPayloadConverterRegistry? converters = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        new SerializationFeature().ConfigureServices(services);

        // Registered after the feature's own registry, so it is the one the serializer resolves.
        if (converters is not null)
            services.AddSingleton(converters);

        return services;
    }
}
