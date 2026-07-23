namespace Elsa.Activities.Primitives.Activities;

/// <summary>Stable wire identifiers owned by the <see cref="PublishEvent"/> durable-first publish surface (spec 135).</summary>
public static class PublishStimulusConstants
{
    /// <summary>The runtime post-commit intent kind a staged <see cref="PublishEvent"/> rides to deliver its stimulus after commit.</summary>
    public const string PublishStimulusIntentKind = "Elsa.Activities.Primitives.PublishStimulus";

    /// <summary>The <c>RequestedBy</c> identity the post-commit handler stamps on the stimulus dispatch request.</summary>
    public const string RequestedBy = "runtime.publish-event";
}
