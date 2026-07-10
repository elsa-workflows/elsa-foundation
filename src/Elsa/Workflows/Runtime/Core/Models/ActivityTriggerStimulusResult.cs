namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The result of asking an <see cref="Elsa.Workflows.Runtime.Core.Contracts.IActivityTriggerStimulusProvider"/>
/// to describe a published trigger node (W7, E3-1; spec 089 D). It separates two states the older
/// "return an empty collection" convention could not tell apart:
/// <list type="bullet">
/// <item><description><see cref="NotRecognized"/> — the provider does not own this activity type; the trigger
/// extractor moves on to the next provider.</description></item>
/// <item><description><see cref="Recognized"/> — the provider owns this activity type and has produced its
/// stimulus descriptors. The descriptor set MAY be empty: a recognized node that <em>declares itself a
/// non-start</em> (e.g. a mid-flow <c>HttpEndpoint</c> with <c>CanStartWorkflow = false</c>) yields zero
/// descriptors, which the extractor turns into zero trigger bindings <em>without</em> failing the publish.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// The distinction matters because the extractor refuses to publish a trigger-shaped node that <em>no</em> provider
/// recognizes (an unroutable trigger that would silently never fire). Before this type an empty collection was
/// overloaded to mean "not mine" alone, with no way to say "mine, but intentionally no bindings" — so an endpoint
/// that opts out of the start role would have tripped that guard. A recognized-but-empty result closes that gap.
/// </remarks>
public sealed class ActivityTriggerStimulusResult
{
    private static readonly IReadOnlyCollection<TriggerStimulusDescriptor> NoDescriptors = [];

    /// <summary>The shared "not my activity type" result — the extractor tries the next provider.</summary>
    public static readonly ActivityTriggerStimulusResult NotRecognized = new(recognized: false, NoDescriptors);

    private ActivityTriggerStimulusResult(bool recognized, IReadOnlyCollection<TriggerStimulusDescriptor> descriptors)
    {
        IsRecognized = recognized;
        Descriptors = descriptors;
    }

    /// <summary>
    /// True when the provider owns this activity type (regardless of how many descriptors it produced). False for
    /// <see cref="NotRecognized"/>.
    /// </summary>
    public bool IsRecognized { get; }

    /// <summary>
    /// The stimulus descriptors for a recognized node — one durable trigger binding each. Empty when the node is
    /// recognized but declares itself a non-start (no bindings, no publish failure), and always empty for
    /// <see cref="NotRecognized"/>.
    /// </summary>
    public IReadOnlyCollection<TriggerStimulusDescriptor> Descriptors { get; }

    /// <summary>
    /// Builds a recognized result carrying <paramref name="descriptors"/>. An empty collection is the deliberate
    /// "recognized, but no trigger bindings" case (the node opted out of the start role).
    /// </summary>
    public static ActivityTriggerStimulusResult Recognized(IReadOnlyCollection<TriggerStimulusDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        return new ActivityTriggerStimulusResult(recognized: true, descriptors);
    }
}
