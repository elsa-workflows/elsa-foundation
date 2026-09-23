using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Nuplane.Services;

/// <summary>Preserves the existing feature-editor activation context in legacy compositions.</summary>
public sealed class LegacyFeatureActivationContextPreparer : IFeatureActivationContextPreparer
{
    public Task<FeatureActivationContext> PrepareAsync(
        FeatureActivationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(context);
    }
}
