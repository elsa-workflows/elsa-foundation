using System.Collections;
using System.Reflection;
using CShells.Features;

namespace Elsa.Studio.Web;

internal sealed class StudioRuntimeFeatureCatalogRefresher(
    IServiceProvider serviceProvider,
    ILogger<StudioRuntimeFeatureCatalogRefresher> logger)
{
    private const string RuntimeFeatureCatalogTypeName = "CShells.Features.RuntimeFeatureCatalog";

    public async Task<StudioRuntimeFeatureCatalogRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var catalogType = ResolveRuntimeFeatureCatalogType();
        var catalog = serviceProvider.GetService(catalogType)
            ?? throw new InvalidOperationException($"The CShells runtime feature catalog service '{RuntimeFeatureCatalogTypeName}' is not registered.");

        var refreshMethod = catalogType.GetMethod(
            "RefreshAsync",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(CancellationToken)],
            modifiers: null)
            ?? throw new InvalidOperationException($"The CShells runtime feature catalog type '{RuntimeFeatureCatalogTypeName}' does not expose RefreshAsync(CancellationToken).");

        if (refreshMethod.Invoke(catalog, [cancellationToken]) is not Task refreshTask)
            throw new InvalidOperationException("CShells runtime feature catalog refresh did not return a task.");

        await refreshTask.ConfigureAwait(false);

        var snapshot = refreshTask.GetType().GetProperty("Result")?.GetValue(refreshTask);
        var generation = ReadLong(snapshot, "Generation");
        var featureCount = ReadCollectionCount(snapshot, "FeatureDescriptors");

        logger.LogDebug(
            "Refreshed CShells runtime feature catalog generation {Generation} with {FeatureCount} feature(s).",
            generation,
            featureCount);

        return new StudioRuntimeFeatureCatalogRefreshResult(generation, featureCount);
    }

    public async Task<IReadOnlyCollection<ShellFeatureDescriptor>> GetFeatureDescriptorsAsync(CancellationToken cancellationToken = default)
    {
        var catalogType = ResolveRuntimeFeatureCatalogType();
        var catalog = serviceProvider.GetService(catalogType)
            ?? throw new InvalidOperationException($"The CShells runtime feature catalog service '{RuntimeFeatureCatalogTypeName}' is not registered.");

        var ensureInitializedMethod = catalogType.GetMethod(
            "EnsureInitializedAsync",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(CancellationToken)],
            modifiers: null)
            ?? throw new InvalidOperationException($"The CShells runtime feature catalog type '{RuntimeFeatureCatalogTypeName}' does not expose EnsureInitializedAsync(CancellationToken).");

        if (ensureInitializedMethod.Invoke(catalog, [cancellationToken]) is not Task ensureInitializedTask)
            throw new InvalidOperationException("CShells runtime feature catalog initialization did not return a task.");

        await ensureInitializedTask.ConfigureAwait(false);

        var snapshot = catalogType.GetProperty("CurrentSnapshot", BindingFlags.Instance | BindingFlags.Public)?.GetValue(catalog)
            ?? throw new InvalidOperationException("CShells runtime feature catalog does not expose a current snapshot.");

        var descriptors = snapshot.GetType().GetProperty("FeatureDescriptors")?.GetValue(snapshot);
        if (descriptors is IEnumerable<ShellFeatureDescriptor> typedDescriptors)
            return typedDescriptors.ToArray();

        if (descriptors is IEnumerable enumerable)
            return enumerable.OfType<ShellFeatureDescriptor>().ToArray();

        throw new InvalidOperationException("CShells runtime feature catalog snapshot does not expose feature descriptors.");
    }

    private static Type ResolveRuntimeFeatureCatalogType()
    {
        var catalogType = AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(assembly => assembly.GetType(RuntimeFeatureCatalogTypeName, throwOnError: false))
            .FirstOrDefault(type => type is not null);

        return catalogType
            ?? throw new InvalidOperationException($"Unable to locate the CShells runtime feature catalog type '{RuntimeFeatureCatalogTypeName}'.");
    }

    private static long? ReadLong(object? instance, string propertyName)
    {
        var value = instance?.GetType().GetProperty(propertyName)?.GetValue(instance);
        return value is null ? null : Convert.ToInt64(value);
    }

    private static int? ReadCollectionCount(object? instance, string propertyName)
    {
        var value = instance?.GetType().GetProperty(propertyName)?.GetValue(instance);

        return value switch
        {
            null => null,
            ICollection collection => collection.Count,
            IEnumerable enumerable => enumerable.Cast<object>().Count(),
            _ => null
        };
    }
}

internal sealed record StudioRuntimeFeatureCatalogRefreshResult(long? Generation, int? FeatureCount);
