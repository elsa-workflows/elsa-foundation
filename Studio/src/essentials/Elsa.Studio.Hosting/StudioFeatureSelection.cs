using CShells;
using CShells.Features;

namespace Elsa.Studio.Hosting;

internal static class StudioFeatureSelection
{
    internal static (string[] Enabled, string[] Disabled) Resolve(ShellSettings settings, IReadOnlyCollection<ShellFeatureDescriptor> features, ILogger logger)
    {
        var descriptors = features.ToDictionary(feature => feature.Id, StringComparer.OrdinalIgnoreCase);
        var enabled = new HashSet<string>(settings.EnabledFeatures, StringComparer.OrdinalIgnoreCase);
        var resolver = new FeatureDependencyResolver();
        foreach (var root in settings.EnabledFeatures.Where(descriptors.ContainsKey))
        {
            try { enabled.UnionWith(resolver.GetOrderedFeatures([root], descriptors)); }
            catch (Exception error) when (error is InvalidOperationException or FeatureNotFoundException)
            {
                // Keep broken configured roots visible for administration; valid roots still expand.
                logger.LogWarning(error, "Studio shell {ShellName} has an invalid dependency graph for {FeatureId}.", settings.Id.Name, root);
            }
        }
        // CShells can reintroduce a raw-disabled feature as a required dependency.
        return (enabled.ToArray(), settings.DisabledFeatures.Except(enabled, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
