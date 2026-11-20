namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Policy-aware preservation of immutable owner inputs; never diagnostic read-model authority.</summary>
public static class RuntimeHistorySourceFacts
{
    public static bool IsFinalProfile(RuntimeHistoryCapturePolicy? policy) => policy is { PolicyId: "runtime-history-v3", Version: 3 };
    public static string EventName(RuntimeHistorySourceFactKind kind) => kind switch
    {
        RuntimeHistorySourceFactKind.IncidentCreated => "IncidentRecorded",
        _ when Enum.IsDefined(kind) => kind.ToString(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public static IReadOnlyList<RuntimeHistorySourceFact>? ForPolicy(RuntimeHistoryCapturePolicy? policy,
        Func<IEnumerable<RuntimeHistorySourceFact>> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return IsFinalProfile(policy) ? ForPolicy(policy, facts()) : null;
    }
    /// <summary>Preserves absent/older pinned policies rather than introducing source facts into their replay identity.</summary>
    public static IReadOnlyList<RuntimeHistorySourceFact>? ForPolicy(RuntimeHistoryCapturePolicy? policy,
        IEnumerable<RuntimeHistorySourceFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!IsFinalProfile(policy)) return null;
        // These prove actual owner decisions even when a pinned vocabulary omits a newer event kind.
        // The capture boundary independently filters emitted events by SupportedKinds.
        // A native cancellation/incident decision can legitimately own more facts than one
        // processing window. Keep every fact in its original logical group and order; the
        // window bounds temporary processing, not the width of an admitted native decision.
        var admitted = new List<RuntimeHistorySourceFact>();
        foreach (var window in facts.Chunk(256)) admitted.AddRange(window);
        return admitted.Count == 0 ? null : Array.AsReadOnly(admitted.ToArray());
    }
}
