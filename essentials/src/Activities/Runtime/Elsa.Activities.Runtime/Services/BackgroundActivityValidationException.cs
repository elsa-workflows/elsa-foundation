using Elsa.Workflows.Runtime.Core.Background;

namespace Elsa.Activities.Runtime.Services;

/// <summary>A deterministic background contract/configuration failure, distinct from provider or ownership loss.</summary>
internal sealed class BackgroundActivityValidationException(string message) : InvalidOperationException(message)
{
    /// <summary>The eligibility criterion a <c>VF-BG-001</c> refusal failed, when it names one.</summary>
    public BackgroundEligibilityCriterion? Criterion { get; init; }
}
