using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Derives the stimulus identity of a named-event trigger (W7, E3-1). It maps an event name to the opaque
/// <c>(StimulusType, StimulusHash)</c> routing pair the engine already uses — it does NOT invent a second
/// routing key. Both the publish-time trigger extractor and the caller that raises an event stimulus derive the
/// hash the same way here, so a published <see cref="Event"/> trigger and an incoming stimulus for the same
/// event name resolve to the same routing key.
/// </summary>
public static class EventStimulus
{
    /// <summary>The stimulus type shared by every named-event trigger.</summary>
    public const string StimulusType = "Event";

    /// <summary>
    /// Computes the deterministic stimulus hash for an event name. The hash is a SHA-256 digest formatted with
    /// the engine's <c>sha256:</c> prefix; the event name is the sole input so the value is stable across
    /// processes and machines.
    /// </summary>
    public static string Hash(string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(eventName));
        return $"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    /// <summary>Builds the trigger stimulus descriptor for an event name and optional passive correlation scope.</summary>
    public static TriggerStimulusDescriptor Describe(string eventName, string? correlationScope = null) =>
        new(StimulusType, Hash(eventName), correlationScope);
}
