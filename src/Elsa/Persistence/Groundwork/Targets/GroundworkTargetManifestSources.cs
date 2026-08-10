using Elsa.Persistence.Groundwork.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Targets;

/// <summary>
/// Resolves the storage-manifest sources bound to one target. Provider admission uses this rather than the
/// full registered set, so a target's capability snapshot and physical schema cover exactly the lanes that
/// live in it.
/// </summary>
public static class GroundworkTargetManifestSources
{
    /// <summary>The manifest sources owned by <paramref name="targetName"/>, in registration order.</summary>
    public static IReadOnlyList<IGroundworkStorageManifestSource> ForTarget(
        IServiceProvider serviceProvider,
        string? targetName)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        var target = GroundworkTargetNames.Normalize(targetName);
        var bindings = serviceProvider.GetRequiredService<GroundworkManifestBindings>();
        return serviceProvider
            .GetServices<IGroundworkStorageManifestSource>()
            .Where(source => string.Equals(
                bindings.TargetFor(source.GetType()),
                target,
                StringComparison.Ordinal))
            .ToArray();
    }
}
