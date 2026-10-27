using CShells.Features;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;

namespace Elsa.Studio.Hosting.Nuplane;

internal sealed class StudioFeatureGuard(IRuntimeFeatureCatalog catalog) : IFeatureActivationGuard
{
    public async Task<FeatureActivationDecision> EvaluateAsync(FeatureActivationContext context, CancellationToken cancellationToken = default)
    {
        var refusals = new List<FeatureActivationRefusal>();
        var enabled = context.Request.Features.Where(feature => feature.Enabled).Select(feature => feature.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!enabled.Contains("StudioApi"))
            refusals.Add(new("StudioApi", "The selected Studio shell must keep StudioApi enabled."));
        var disabled = context.Request.Features.Where(feature => !feature.Enabled).Select(feature => feature.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var snapshot = await catalog.GetSnapshotAsync(cancellationToken);
        foreach (var feature in snapshot.FeatureDescriptors.Where(feature => enabled.Contains(feature.Id)))
            foreach (var dependency in feature.Dependencies.Where(disabled.Contains))
                refusals.Add(new(feature.Id, $"Feature '{feature.Id}' requires '{dependency}', which this request explicitly disables."));
        return new(refusals);
    }
}
