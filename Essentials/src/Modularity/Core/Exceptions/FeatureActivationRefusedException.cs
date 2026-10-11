using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Core.Exceptions;

/// <summary>
/// At least one <see cref="Contracts.IFeatureActivationGuard"/> refused the request, so nothing was saved,
/// refreshed or reloaded (spec 171 FR-060, FR-067). Its own type — like
/// <see cref="FeatureCatalogRevisionConflictException"/>, and ahead of that one's base
/// <see cref="InvalidOperationException"/> in the API's fault ladder — so a refusal maps to an HTTP 409
/// rather than being reported as a malformed request.
/// </summary>
public sealed class FeatureActivationRefusedException(IReadOnlyList<FeatureActivationRefusal> refusals)
    : InvalidOperationException(Describe(refusals))
{
    /// <summary>Every refusal, across every guard, in the order the guards reported them.</summary>
    public IReadOnlyList<FeatureActivationRefusal> Refusals { get; } = refusals;

    private static string Describe(IReadOnlyList<FeatureActivationRefusal> refusals)
    {
        ArgumentNullException.ThrowIfNull(refusals);
        return refusals.Count == 1
            ? refusals[0].Reason
            : $"{refusals.Count} feature(s) could not be enabled and nothing was saved:{Environment.NewLine}" +
              string.Join(Environment.NewLine, refusals.Select(refusal => $"- {refusal.Reason}"));
    }
}
