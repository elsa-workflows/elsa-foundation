using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Core.Contracts;

/// <summary>
/// Prepares one complete candidate activation context before ordinary activation guards run.
/// This is a single replaceable service, not a collection of mutating preprocessors.
/// </summary>
/// <remarks>
/// A preparation refusal throws <see cref="Elsa.Modularity.Core.Exceptions.FeatureActivationRefusedException"/>
/// with redacted <see cref="FeatureActivationRefusal"/> entries before any save or reload.
/// </remarks>
public interface IFeatureActivationContextPreparer
{
    /// <summary>Returns the context to evaluate; the default implementation returns it unchanged.</summary>
    Task<FeatureActivationContext> PrepareAsync(
        FeatureActivationContext context,
        CancellationToken cancellationToken = default);
}
