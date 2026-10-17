using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Core.Extensions;

/// <summary>Registers runtime capability evidence for activity-family feature authors.</summary>
public static class RuntimeActivityConsumerServiceCollectionExtensions
{
    /// <summary>Advertises a consumer and the descriptor schema versions it supports.</summary>
    /// <remarks>
    /// Repeated calls with identical declarations register once, regardless of version ordering or repetition.
    /// Distinct consumers can share the same implementation type. Additional schema declarations for
    /// one consumer remain additive; requirement checking unions them by ordinal consumer key.
    /// Capability evidence is static for the application lifetime and is therefore registered as a singleton.
    /// </remarks>
    public static IServiceCollection AddRuntimeActivityConsumerCapability(
        this IServiceCollection services, string consumerKey, IEnumerable<string> supportedSchemaVersions)
    {
        ArgumentNullException.ThrowIfNull(services);
        var capability = new RuntimeActivityConsumerCapability(consumerKey, supportedSchemaVersions);
        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(IRuntimeActivityConsumerCapability) &&
                !descriptor.IsKeyedService &&
                descriptor.ImplementationInstance is RuntimeActivityConsumerCapability existing &&
                StringComparer.Ordinal.Equals(existing.ConsumerKey, capability.ConsumerKey) &&
                existing.SupportedSchemaVersions.SequenceEqual(capability.SupportedSchemaVersions, StringComparer.Ordinal)))
            services.AddSingleton<IRuntimeActivityConsumerCapability>(capability);
        return services;
    }
}
