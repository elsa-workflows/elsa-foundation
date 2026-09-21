using System.Collections.Concurrent;

namespace Elsa.Api.Compatibility.Testing.Collectibility;

/// <summary>
/// Names the publication seam that intentionally keeps a collectible endpoint alive.
/// </summary>
public enum RetentionStage
{
    Clean = 0,
    Route = 1,
    Services = 2,
    Serializer = 3,
    Harness = 4
}

/// <summary>
/// Publishes a single strong reference for a cycle so unload diagnostics can identify the
/// framework seam responsible for retention. The probe is deliberately keyed by an opaque
/// cycle id; no probe state is exposed to the evidence object.
/// </summary>
public static class RetentionStageProbe
{
    private static readonly ConcurrentDictionary<Guid, Retention> Active = new();

    public static void PublishRoute(Guid cycleId, object collectibleValue) => Publish(cycleId, RetentionStage.Route, collectibleValue);

    public static void PublishServices(Guid cycleId, object collectibleValue) => Publish(cycleId, RetentionStage.Services, collectibleValue);

    public static void PublishSerializer(Guid cycleId, object collectibleValue) => Publish(cycleId, RetentionStage.Serializer, collectibleValue);

    public static void PublishHarness(Guid cycleId, object collectibleValue) => Publish(cycleId, RetentionStage.Harness, collectibleValue);

    /// <summary>Releases the deliberate reference for a cycle. It is safe to call repeatedly.</summary>
    public static void Release(Guid cycleId) => Active.TryRemove(cycleId, out _);

    internal static RetentionStage PublishedStage(Guid cycleId) =>
        Active.TryGetValue(cycleId, out var retention) ? retention.Stage : RetentionStage.Clean;

    private static void Publish(Guid cycleId, RetentionStage stage, object collectibleValue)
    {
        ArgumentNullException.ThrowIfNull(collectibleValue);
        if (stage is RetentionStage.Clean)
            throw new ArgumentOutOfRangeException(nameof(stage), "Clean cycles do not publish a retention reference.");

        if (!Active.TryAdd(cycleId, new Retention(stage, collectibleValue)))
            throw new InvalidOperationException($"A retention reference has already been published for cycle '{cycleId}'.");
    }

    private sealed record Retention(RetentionStage Stage, object StrongReference);
}
