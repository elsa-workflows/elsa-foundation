using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Elsa.Modularity.Nuplane.Services;

internal static class RuntimeFeatureCatalogReflection
{
    public static async Task<object> RefreshAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var catalogType = AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(assembly => assembly.GetType("CShells.Features.RuntimeFeatureCatalog", throwOnError: false))
            .FirstOrDefault(type => type is not null)
            ?? throw new InvalidOperationException("Could not find the CShells runtime feature catalog type.");

        var catalog = serviceProvider.GetRequiredService(catalogType);
        var refreshMethod = catalogType.GetMethod("RefreshAsync", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Could not find RuntimeFeatureCatalog.RefreshAsync.");

        var refreshTask = refreshMethod.Invoke(catalog, [cancellationToken]) as Task
            ?? throw new InvalidOperationException("RuntimeFeatureCatalog.RefreshAsync did not return a task.");

        await refreshTask.ConfigureAwait(false);

        return refreshTask.GetType().GetProperty("Result")?.GetValue(refreshTask)
            ?? throw new InvalidOperationException("RuntimeFeatureCatalog.RefreshAsync did not return a snapshot.");
    }

    public static int GetFeatureDescriptorCount(object snapshot)
    {
        var featureDescriptors = snapshot.GetType().GetProperty("FeatureDescriptors")?.GetValue(snapshot)
            ?? throw new InvalidOperationException("The runtime feature catalog snapshot did not expose feature descriptors.");

        return Convert.ToInt32(featureDescriptors.GetType().GetProperty("Count")?.GetValue(featureDescriptors));
    }

    public static IEnumerable<object> EnumerateFeatureDescriptors(object snapshot)
    {
        var featureDescriptors = snapshot.GetType().GetProperty("FeatureDescriptors")?.GetValue(snapshot);
        return featureDescriptors is System.Collections.IEnumerable enumerable
            ? enumerable.Cast<object>()
            : [];
    }
}
