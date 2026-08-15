using System.Runtime.CompilerServices;

namespace Elsa.Api.Compatibility.Testing.Collectibility;

/// <summary>
/// Weak-reference-only unload evidence for one collectible endpoint cycle.
/// </summary>
public sealed class UnloadEvidence
{
    public const int DefaultMaxCollectionAttempts = 12;
    private const int MaximumCollectionAttempts = 32;

    private UnloadEvidence(
        Guid cycle,
        RetentionStage stage,
        WeakReference loadContext,
        WeakReference assembly,
        WeakReference endpointType,
        bool collected,
        int collectionAttempts,
        string? diagnostic)
    {
        Cycle = cycle;
        Stage = stage;
        LoadContext = loadContext;
        Assembly = assembly;
        EndpointType = endpointType;
        Collected = collected;
        CollectionAttempts = collectionAttempts;
        Diagnostic = diagnostic;
    }

    public Guid Cycle { get; }

    /// <summary>The stage that still owns a strong reference, or <see cref="RetentionStage.Clean"/>.</summary>
    public RetentionStage Stage { get; }

    public WeakReference LoadContext { get; }

    public WeakReference Assembly { get; }

    public WeakReference EndpointType { get; }

    public bool Collected { get; }

    public int CollectionAttempts { get; }

    /// <summary>
    /// A short, stable classification. It contains only static text and never includes a loaded
    /// assembly, type, route object, service object, or serializer object.
    /// </summary>
    public string? Diagnostic { get; }

    public static UnloadEvidence Verify(CollectibleEndpointCycle cycle, int maxAttempts = DefaultMaxCollectionAttempts)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        return Verify(cycle.CycleId, cycle.LoadContext, cycle.Assembly, cycle.EndpointType, maxAttempts);
    }

    /// <summary>
    /// Verifies arbitrary collectible endpoint evidence without requiring the caller to expose
    /// framework-specific route or service owners through the shared compatibility model.
    /// </summary>
    public static UnloadEvidence Verify(
        Guid cycleId,
        WeakReference loadContext,
        WeakReference assembly,
        WeakReference endpointType,
        int maxAttempts = DefaultMaxCollectionAttempts)
    {
        ArgumentNullException.ThrowIfNull(loadContext);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(endpointType);
        if (maxAttempts is < 1 or > MaximumCollectionAttempts)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts,
                $"Collection attempts must be between 1 and {MaximumCollectionAttempts}.");

        var collected = false;
        var attempts = 0;
        for (; attempts < maxAttempts; attempts++)
        {
            ForceCollection();
            if (!loadContext.IsAlive && !assembly.IsAlive && !endpointType.IsAlive)
            {
                collected = true;
                attempts++;
                break;
            }
        }

        var stage = collected ? RetentionStage.Clean : RetentionStageProbe.PublishedStage(cycleId);
        var diagnostic = collected ? null : Describe(stage);
        return new UnloadEvidence(
            cycleId,
            stage,
            loadContext,
            assembly,
            endpointType,
            collected,
            attempts,
            diagnostic);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static string Describe(RetentionStage stage) => stage switch
    {
        RetentionStage.Route => "route retention",
        RetentionStage.Services => "DI/services retention",
        RetentionStage.Serializer => "serializer retention",
        RetentionStage.Harness => "harness retention",
        _ => "harness retention (unexpected collectible reference)"
    };
}
