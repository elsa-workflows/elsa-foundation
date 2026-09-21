using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Core.Contracts;

/// <summary>
/// Decides whether an apply request may be saved (spec 171 FR-059, ADR 0076 D9).
/// <c>FeatureManagementService.ApplyAsync</c> calls every registered guard after it has validated the
/// request and before it saves it, so a refusal stops the save — and with it the runtime-catalog refresh
/// and the shell reload — rather than reporting a problem about configuration that is already stored.
/// </summary>
/// <remarks>
/// <para>
/// A guard receives the request with its secret settings already restored to their real values
/// (<c>RestoreSecrets</c> runs before validation), so it is handling live credentials. It MUST NOT put a
/// connection string — or any other restored secret — into a refusal, an exception, or a log line
/// (FR-061): a refusal names the feature and what it depends on, and nothing else.
/// </para>
/// <para>
/// A guard that throws also prevents the save, because <c>ApplyAsync</c> does not catch it. Throwing is
/// therefore never a way to allow something through; it is an unclassified refusal that surfaces as a
/// fault instead of a 409. A guard that means "no" returns a refusal.
/// </para>
/// </remarks>
public interface IFeatureActivationGuard
{
    /// <summary>
    /// Reports every reason this request must not be saved, or
    /// <see cref="FeatureActivationDecision.Allowed"/> when this guard has none.
    /// </summary>
    Task<FeatureActivationDecision> EvaluateAsync(FeatureActivationContext context, CancellationToken cancellationToken = default);
}
