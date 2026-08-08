using Elsa.Events.Core.Extensions;
using Elsa.Persistence.Groundwork.Composition;
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
    /// Adds idempotent defaults. A host may register its own naming policy before this method is called.
    /// </summary>
    public static IServiceCollection AddGroundworkStorageComposition(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(GroundworkStorageNamingPolicyOptions.Identity);
        services.TryAddSingleton<GroundworkProviderCapabilityAdmission>();
        services.TryAddScoped<GroundworkStorageCompositionValidator>();
        services.TryAddScoped<GroundworkStorageCompositionHandler>();
        services.TryAddEventHandler<GroundworkStorageComposing, GroundworkStorageCompositionHandler>();
        services.TryAddScoped(sp => new GroundworkStorageCompositionFactory(
            sp.GetRequiredService<GroundworkStorageCompositionHandler>(),
            sp.GetRequiredService<GroundworkStorageCompositionValidator>(),
            sp.GetRequiredService<GroundworkStorageNamingPolicyOptions>(),
            sp.GetService<Elsa.Events.Core.Contracts.IInlineEventPublisher>(),
            sp.GetService<GroundworkDeploymentSchemaSelection>()));
        return services;
    }
}
