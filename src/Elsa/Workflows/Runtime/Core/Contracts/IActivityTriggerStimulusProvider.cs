using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Describes the stimuli a start-trigger activity reacts to, at publish time (W7, E3-1). One provider is
/// registered per trigger activity type (e.g. the event/signal trigger); the trigger extractor asks each
/// provider to describe a node it recognizes. A provider returns
/// <see cref="ActivityTriggerStimulusResult.NotRecognized"/> for an activity type it does not own — the
/// extractor then moves on to the next provider.
/// </summary>
/// <remarks>
/// Registered providers form a context-selected Strategy set, not a contribution fan-in: every classified node
/// must be recognized by exactly one provider, and registration order never selects a winner.
///
/// Providers read only the published <see cref="ExecutableNode"/> (its literal input bindings), never a
/// running workflow, so the stimulus identity is fixed at publish time exactly as Elsa 4's pinned-executable
/// model requires. A provider whose trigger carries a non-literal, unresolvable stimulus key throws, which
/// fails the publish rather than persisting an unroutable trigger. A recognized node may yield more than one
/// descriptor (e.g. an HTTP endpoint that reacts to several methods produces one descriptor per method); the
/// extractor turns each into its own <see cref="WorkflowTriggerBinding"/>. A recognized node may also yield
/// <em>zero</em> descriptors when it declares itself a non-start (spec 089 D: a mid-flow <c>HttpEndpoint</c>
/// with <c>CanStartWorkflow = false</c>) — that produces no trigger bindings and, crucially, does not fail the
/// publish, which a bare empty collection could not express (it was indistinguishable from "not mine").
/// </remarks>
public interface IActivityTriggerStimulusProvider
{
    /// <summary>
    /// Gets the stable, non-secret identity of this strategy. Existing providers receive a deterministic fallback
    /// derived from their public CLR type identity; first-party providers should override it with an explicit id.
    /// </summary>
    string ProviderId => GetType().FullName ?? GetType().Name;

    /// <summary>
    /// Returns <see cref="ActivityTriggerStimulusResult.Recognized"/> carrying the stimulus identities for
    /// <paramref name="node"/> if this provider owns its activity type (zero or more descriptors); otherwise
    /// <see cref="ActivityTriggerStimulusResult.NotRecognized"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    ActivityTriggerStimulusResult Describe(ExecutableNode node);
}
