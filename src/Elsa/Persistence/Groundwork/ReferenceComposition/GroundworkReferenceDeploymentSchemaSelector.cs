using CShells.Features;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Groundwork.DiagnosticRecords;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.ReferenceComposition;

/// <summary>
/// Selects the reference deployment schema from the schema-affecting features enabled for one shell.
/// </summary>
public static class GroundworkReferenceDeploymentSchemaSelector
{
    /// <summary>
    /// Registers the exact reference deployment schema required by the enabled shell features.
    /// </summary>
    public static IServiceCollection AddGroundworkReferenceDeploymentSchema(
        this IServiceCollection services,
        ShellFeatureContext context)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Select(context) switch
        {
            var type when type == typeof(GroundworkAllFeaturesWithIdentityAndDiagnosticsDeploymentSchema) =>
                services.AddGroundworkReferenceDeploymentSchema<GroundworkAllFeaturesWithIdentityAndDiagnosticsDeploymentSchema>(),
            var type when type == typeof(GroundworkAllFeaturesWithDiagnosticsDeploymentSchema) =>
                services.AddGroundworkReferenceDeploymentSchema<GroundworkAllFeaturesWithDiagnosticsDeploymentSchema>(),
            var type when type == typeof(GroundworkAllFeaturesWithIdentityDeploymentSchema) =>
                services.AddGroundworkReferenceDeploymentSchema<GroundworkAllFeaturesWithIdentityDeploymentSchema>(),
            _ => services.AddGroundworkReferenceDeploymentSchema<GroundworkAllFeaturesDeploymentSchema>()
        };
    }

    /// <summary>
    /// Registers an explicit reference deployment schema and exposes its diagnostic deployment contract
    /// when the selected source owns diagnostic streams.
    /// </summary>
    public static IServiceCollection AddGroundworkReferenceDeploymentSchema<TDeploymentSource>(
        this IServiceCollection services)
        where TDeploymentSource : GroundworkDeploymentSchemaManifestSource, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddGroundworkStorageComposition<TDeploymentSource>();
        if (typeof(IDiagnosticRecordDeploymentManifestSource).IsAssignableFrom(typeof(TDeploymentSource)))
        {
            services.TryAddSingleton<IDiagnosticRecordDeploymentManifestSource>(provider =>
                (IDiagnosticRecordDeploymentManifestSource)provider.GetRequiredService<TDeploymentSource>());
        }

        return services;
    }

    /// <summary>
    /// Returns the public deployment schema source type required by the enabled shell features.
    /// </summary>
    public static Type Select(ShellFeatureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var enabledFeatureIds = context.Settings.EnabledFeatures.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var schemaFeatures = context.AllFeatures
            .Where(descriptor => enabledFeatureIds.Contains(descriptor.Id))
            .Select(descriptor => new
            {
                descriptor.Id,
                Marker = descriptor.Metadata.TryGetValue(GroundworkSchemaFeatureMetadata.Key, out var marker)
                    ? marker as string
                    : null,
                HasMarker = descriptor.Metadata.ContainsKey(GroundworkSchemaFeatureMetadata.Key)
            })
            .Where(feature => feature.HasMarker)
            .OrderBy(feature => feature.Id, StringComparer.Ordinal)
            .ToArray();

        var unsupported = schemaFeatures
            .Where(feature => !IsSupportedMarker(feature.Marker))
            .ToArray();
        if (unsupported.Length > 0)
        {
            var descriptions = unsupported.Select(feature =>
                $"'{feature.Id}' ('{feature.Marker ?? "<non-string-or-null>"}')");
            throw new InvalidOperationException(
                $"Enabled shell features declare unsupported Groundwork schema markers: {string.Join(", ", descriptions)}. " +
                $"Register a deployment schema mapping for every '{GroundworkSchemaFeatureMetadata.Key}' value before enabling the feature.");
        }

        var identityEnabled = schemaFeatures.Any(feature => string.Equals(
            feature.Marker,
            GroundworkSchemaFeatureMetadata.Identity,
            StringComparison.Ordinal));
        var diagnosticsEnabled = schemaFeatures.Any(feature => string.Equals(
            feature.Marker,
            GroundworkSchemaFeatureMetadata.Diagnostics,
            StringComparison.Ordinal));

        return (identityEnabled, diagnosticsEnabled) switch
        {
            (true, true) => typeof(GroundworkAllFeaturesWithIdentityAndDiagnosticsDeploymentSchema),
            (true, false) => typeof(GroundworkAllFeaturesWithIdentityDeploymentSchema),
            (false, true) => typeof(GroundworkAllFeaturesWithDiagnosticsDeploymentSchema),
            _ => typeof(GroundworkAllFeaturesDeploymentSchema)
        };
    }

    private static bool IsSupportedMarker(string? marker) =>
        string.Equals(marker, GroundworkSchemaFeatureMetadata.Identity, StringComparison.Ordinal) ||
        string.Equals(marker, GroundworkSchemaFeatureMetadata.Diagnostics, StringComparison.Ordinal);

}
