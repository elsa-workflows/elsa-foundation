using CShells.Features;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Serialization.Core;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>
/// Default <see cref="IActivityFeatureAttributionResolver"/>: maps an activity type key to a live CLR type
/// via the well-known type registry, then maps the type's assembly to the shell feature whose startup type
/// lives in that assembly (from the CShells runtime feature catalog). Both dependencies are optional so a
/// shell that composes neither the serialization feature nor a CShells host degrades to null attribution
/// instead of failing catalog reads.
/// </summary>
/// <remarks>
/// KNOWN DUPLICATION (accepted, tracked): the build-time contract generator
/// (<c>tools/contracts/Elsa.Contracts.Generator</c>, <c>FragmentProjector.Project</c>) restates this
/// attribution rule — assembly → owning feature, ties broken by the min-ordinal feature id — because this
/// resolver's dependencies are runtime-only and unavailable to a build-time projection. The two are a
/// two-line rule rather than a shared parser (contrast <c>ActivityPortDesignFacetReader</c>, which unifies
/// the ports facet convention), so §2.17 applies; if the tie-break changes here, change it there too or
/// fragments and the served catalog will attribute activities differently.
/// </remarks>
public sealed class ActivityFeatureAttributionResolver(
    IWellKnownTypeRegistry? typeRegistry = null,
    IRuntimeFeatureCatalog? featureCatalog = null) : IActivityFeatureAttributionResolver
{
    // Built once per resolver instance (scoped lifetime). A benign race merely computes the index twice;
    // that is thread-safe enough for scoped use.
    private Dictionary<string, string>? _featureIdByAssemblyName;

    public async ValueTask<string?> ResolveProvidingFeatureAsync(string activityTypeKey, CancellationToken cancellationToken)
    {
        if (typeRegistry is null || featureCatalog is null)
            return null;

        if (!typeRegistry.TryGetType(activityTypeKey, out var type))
            return null;

        var index = _featureIdByAssemblyName ??= await BuildIndexAsync(featureCatalog, cancellationToken);
        var assemblyName = type.Assembly.GetName().Name;
        return assemblyName is not null && index.TryGetValue(assemblyName, out var featureId) ? featureId : null;
    }

    private static async Task<Dictionary<string, string>> BuildIndexAsync(
        IRuntimeFeatureCatalog featureCatalog,
        CancellationToken cancellationToken)
    {
        var snapshot = await featureCatalog.GetSnapshotAsync(cancellationToken);
        return snapshot.FeatureDescriptors
            .Where(descriptor => !string.IsNullOrWhiteSpace(descriptor.Id))
            .Select(descriptor => (AssemblyName: descriptor.StartupType?.Assembly.GetName().Name, FeatureId: descriptor.Id))
            .Where(entry => entry.AssemblyName is not null)
            .GroupBy(entry => entry.AssemblyName!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                // Multiple features in one assembly: keep the first ordinal-sorted id for determinism.
                group => group.Select(entry => entry.FeatureId).Min(StringComparer.Ordinal)!,
                StringComparer.Ordinal);
    }
}
