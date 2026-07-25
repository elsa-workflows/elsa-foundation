using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Core.Extensions;

/// <summary>Registers incident-strategy descriptors, implementations, and the descriptor-only registry.</summary>
public static class IncidentStrategyServiceCollectionExtensions
{
    /// <summary>Registers an incident strategy and its immutable discovery descriptor.</summary>
    public static IServiceCollection AddIncidentStrategy<TStrategy>(
        this IServiceCollection services,
        IncidentStrategyDescriptor descriptor)
        where TStrategy : class, IIncidentStrategy
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(descriptor);

        IncidentStrategyIdentity.ValidateDescriptor(descriptor);
        var strategyType = typeof(TStrategy);
        var matchingIdentity = services
            .Where(service => service.ServiceType == typeof(IncidentStrategyRegistration))
            .Select(service => service.ImplementationInstance)
            .OfType<IncidentStrategyRegistration>()
            .Where(registration => registration.Descriptor.Reference.Equals(descriptor.Reference))
            .ToArray();

        if (matchingIdentity.Length > 0)
            throw new InvalidOperationException(
                $"Incident strategy '{descriptor.Reference}' is already registered by {string.Join(", ", matchingIdentity.Select(RegistrationIdentity).Order(StringComparer.Ordinal))}; duplicate identities are not allowed.");

        var existingStrategyServices = services
            .Where(service => service.ServiceType == strategyType)
            .ToArray();
        if (existingStrategyServices.Length > 1)
            throw new InvalidOperationException($"Incident strategy type '{StrategyTypeIdentity(strategyType)}' has multiple service registrations; exactly one scoped registration is required.");
        if (existingStrategyServices is [var existingStrategyService] &&
            existingStrategyService.Lifetime != ServiceLifetime.Scoped)
        {
            throw new InvalidOperationException(
                $"Incident strategy type '{StrategyTypeIdentity(strategyType)}' is registered with lifetime '{existingStrategyService.Lifetime}'; incident strategies must be scoped.");
        }

        services.AddIncidentStrategyRegistry();
        if (existingStrategyServices.Length == 0)
            services.AddScoped<TStrategy>();
        services.AddSingleton(new IncidentStrategyRegistration(descriptor, strategyType));

        return services;
    }

    /// <summary>Registers an incident strategy using its one non-inherited <see cref="IncidentStrategyAttribute"/>.</summary>
    public static IServiceCollection AddIncidentStrategy<TStrategy>(this IServiceCollection services)
        where TStrategy : class, IIncidentStrategy
    {
        ArgumentNullException.ThrowIfNull(services);

        var strategyType = typeof(TStrategy);
        var attributes = strategyType.GetCustomAttributes(typeof(IncidentStrategyAttribute), inherit: false)
            .Cast<IncidentStrategyAttribute>()
            .ToArray();
        if (attributes.Length != 1)
        {
            throw new InvalidOperationException(
                $"Incident strategy type '{StrategyTypeIdentity(strategyType)}' must declare exactly one non-inherited {nameof(IncidentStrategyAttribute)}.");
        }

        var attribute = attributes[0];
        var displayName = attribute.DisplayName ?? Humanize(strategyType.Name);
        return services.AddIncidentStrategy<TStrategy>(
            new IncidentStrategyDescriptor(
                new IncidentStrategyReference(attribute.Alias, attribute.Version),
                displayName,
                attribute.Description));
    }

    /// <summary>Registers the descriptor-only catalog and scoped implementation resolver.</summary>
    public static IServiceCollection AddIncidentStrategyRegistry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IncidentStrategyCatalog>();
        services.TryAddSingleton<IIncidentStrategyCatalog>(serviceProvider => serviceProvider.GetRequiredService<IncidentStrategyCatalog>());
        services.TryAddSingleton<IIncidentStrategyRegistrationLookup>(serviceProvider => serviceProvider.GetRequiredService<IncidentStrategyCatalog>());
        services.TryAddScoped<IIncidentStrategyResolver>(serviceProvider => new IncidentStrategyResolver(
            serviceProvider.GetRequiredService<IIncidentStrategyRegistrationLookup>(),
            serviceProvider));
        return services;
    }

    /// <summary>Registers one namespaced post-commit intent kind that incident strategies may request.</summary>
    public static IServiceCollection AddIncidentStrategySafeIntent(
        this IServiceCollection services,
        IncidentStrategySafeIntentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(descriptor);

        IncidentStrategyIdentity.ValidateSafeIntentKind(descriptor.Kind);
        var matches = services
            .Where(service => service.ServiceType == typeof(IncidentStrategySafeIntentRegistration))
            .Select(service => service.ImplementationInstance)
            .OfType<IncidentStrategySafeIntentRegistration>()
            .Where(registration => StringComparer.Ordinal.Equals(registration.Descriptor.Kind, descriptor.Kind))
            .ToArray();

        if (matches.Any(registration => !StringComparer.Ordinal.Equals(registration.Descriptor.Description, descriptor.Description)))
        {
            throw new InvalidOperationException(
                $"Incident-strategy safe intent kind '{descriptor.Kind}' has conflicting registrations.");
        }

        if (matches.Length == 0)
            services.AddSingleton(new IncidentStrategySafeIntentRegistration(descriptor));

        return services;
    }

    private static string RegistrationIdentity(IncidentStrategyRegistration registration) =>
        $"{registration.Descriptor.Reference} => {StrategyTypeIdentity(registration.StrategyType)}";

    private static string StrategyTypeIdentity(Type type) => type.FullName ?? type.Name;

    private static string Humanize(string typeName)
    {
        const string suffix = "IncidentStrategy";
        var name = typeName.EndsWith(suffix, StringComparison.Ordinal) ? typeName[..^suffix.Length] : typeName;
        return string.Concat(name.Select((character, index) =>
            index > 0 && char.IsUpper(character) && !char.IsUpper(name[index - 1]) ? $" {character}" : character.ToString()));
    }
}
