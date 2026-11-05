namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Semantic owners present in this composition. Only new admissions use these declarations.</summary>
public sealed class RuntimeHistorySourceSupport
{
    public RuntimeHistorySourceSupport(IReadOnlyList<RuntimeHistorySourceFactKind> kinds, bool timerOwner = false)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        if (kinds.Any(kind => !Enum.IsDefined(kind))) throw new ArgumentException("Unknown history source kind.", nameof(kinds));
        Kinds = Array.AsReadOnly(kinds.Distinct().ToArray()); TimerOwner = timerOwner;
    }
    public IReadOnlyList<RuntimeHistorySourceFactKind> Kinds { get; }
    public bool TimerOwner { get; }
}

public static class RuntimeHistorySourceFacts
{
    /// <summary>Preserves absent/older pinned policies rather than introducing source facts into their replay identity.</summary>
    public static IReadOnlyList<RuntimeHistorySourceFact>? ForPolicy(RuntimeHistoryCapturePolicy? policy,
        IEnumerable<RuntimeHistorySourceFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (policy is null || policy.PolicyId == "runtime-history-v1" && policy.Version == 1) return null;
        var admitted = facts.Where(fact => policy.SupportedKinds.Contains(fact.Kind.ToString(), StringComparer.Ordinal)).ToArray();
        if (admitted.Length > 256) throw new InvalidOperationException("One logical checkpoint exceeds its semantic source-fact bound.");
        return admitted.Length == 0 ? null : Array.AsReadOnly(admitted);
    }
}
