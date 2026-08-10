using Elsa.Events.Core.Extensions;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.SchemaEvolution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.Unified.DependencyInjection;

/// <summary>Registers the provider-neutral host-selected composition pipeline.</summary>
public static class GroundworkStorageCompositionRegistration
{
    /// <summary>
    /// Registers a public parameterless deployment source as the single authority for both the
    /// runtime-selected manifest sources and Groundwork.Tool.
    /// </summary>
    public static IServiceCollection AddGroundworkStorageComposition<TDeploymentSource>(
        this IServiceCollection services)
        where TDeploymentSource : GroundworkDeploymentSchemaManifestSource, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        var selectedType = typeof(TDeploymentSource);
        var existingSelection = services
            .Where(descriptor => descriptor.ServiceType == typeof(GroundworkDeploymentSchemaSelection))
            .Select(descriptor => descriptor.ImplementationInstance as GroundworkDeploymentSchemaSelection)
            .FirstOrDefault(selection => selection is not null);
        if (existingSelection is not null && existingSelection.SourceType != selectedType)
        {
            var selections = new[] { existingSelection.SourceType, selectedType }
                .Select(type => type.FullName ?? type.Name)
                .Order(StringComparer.Ordinal);
            throw new InvalidOperationException(
                $"Conflicting Groundwork deployment schema sources were selected: {string.Join(", ", selections.Select(identity => $"'{identity}'"))}. " +
                "Register exactly one deployment schema source per host.");
        }

        if (existingSelection is not null)
            return services.AddGroundworkStorageComposition();

        var source = new TDeploymentSource();
        var namingPolicy = source.GetStorageNamingPolicy();
        services.AddSingleton(new GroundworkDeploymentSchemaSelection(
            selectedType,
            source.GetDeclarations(),
            namingPolicy));
        foreach (var sourceType in source.GetManifestSourceTypes())
        {
            services.TryAddEnumerable(ServiceDescriptor.Scoped(
                typeof(IGroundworkStorageManifestSource),
                sourceType));
        }

        services.RemoveAll<GroundworkStorageNamingPolicyOptions>();
        services.AddSingleton(namingPolicy);
        services.TryAddSingleton(source);
        services.RemoveAll<IPhysicalSchemaManifestSource>();
        services.AddSingleton<IPhysicalSchemaManifestSource>(sp => sp.GetRequiredService<TDeploymentSource>());
        return services.AddGroundworkStorageComposition();
    }

    /// <summary>
    /// Registers the capability-admission holder for one target. Capability evidence is published once per
    /// admitted store, so a host with two targets keeps two independent snapshots and one target's evidence
    /// can never stand in for another's. The default target is also exposed unkeyed for the consumers that
    /// resolve admission without knowing about targets.
    /// </summary>
    public static IServiceCollection AddGroundworkProviderCapabilityAdmission(
        this IServiceCollection services,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var target = GroundworkTargetNames.Normalize(targetName);
        services.TryAddKeyedSingleton<GroundworkProviderCapabilityAdmission>(target);
        if (GroundworkTargetNames.IsDefault(target))
        {
            services.TryAddSingleton(serviceProvider =>
                serviceProvider.GetRequiredKeyedService<GroundworkProviderCapabilityAdmission>(target));
        }

        return services;
    }

    /// <summary>
    /// Adds idempotent defaults. A host may register its own naming policy before this method is called.
    /// </summary>
    public static IServiceCollection AddGroundworkStorageComposition(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(GroundworkStorageNamingPolicyOptions.Identity);
        services.GroundworkManifestBindings();
        services.TryAddScoped<GroundworkStorageCompositionValidator>();
        services.TryAddScoped<GroundworkStorageCompositionHandler>();
        services.TryAddEventHandler<OnGroundworkStorageComposing, GroundworkStorageCompositionHandler>();
        services.TryAddScoped(sp => new GroundworkStorageCompositionFactory(
            sp.GetRequiredService<GroundworkStorageCompositionHandler>(),
            sp.GetRequiredService<GroundworkStorageCompositionValidator>(),
            sp.GetRequiredService<GroundworkStorageNamingPolicyOptions>(),
            sp.GetService<Elsa.Events.Core.Contracts.IInlineEventPublisher>(),
            sp.GetServices<IGroundworkStorageManifestSource>(),
            sp.GetRequiredService<GroundworkManifestBindings>(),
            sp.GetService<GroundworkDeploymentSchemaSelection>()));
        return services;
    }
}
