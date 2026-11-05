namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>At most the original schedule and terminal owner facts, retained on their existing timer source until acknowledged.</summary>
public sealed record DurableTimerHistoryResponsibility
{
    public DurableTimerHistoryResponsibility(RuntimeHistoryCapturePolicy policy, IReadOnlyList<RuntimeHistoryEvent> pendingEvents, bool retired)
    {
        ArgumentNullException.ThrowIfNull(policy); ArgumentNullException.ThrowIfNull(pendingEvents);
        if (pendingEvents.Count > 2 || pendingEvents.Select(e => e.HistoryEventId).Distinct(StringComparer.Ordinal).Count() != pendingEvents.Count)
            throw new ArgumentException("A timer retains only its unique schedule and terminal history responsibility.", nameof(pendingEvents));
        foreach (var e in pendingEvents)
        {
            RuntimeHistorySafety.ValidateMetadata(e);
            if (e.Sequence != 0 || e.PhysicalCommitId is not null || e.RecordedAt is not null ||
                e.Authority != RuntimeHistoryAuthority.DeliveryObservation || !policy.SupportedKinds.Contains(e.Name, StringComparer.Ordinal))
                throw new ArgumentException("Timer history must remain safe, unpositioned and admitted by its pinned policy.", nameof(pendingEvents));
        }
        Policy = policy; PendingEvents = Array.AsReadOnly(pendingEvents.ToArray()); Retired = retired;
    }
    public RuntimeHistoryCapturePolicy Policy { get; }
    public IReadOnlyList<RuntimeHistoryEvent> PendingEvents { get; }
    public bool Retired { get; }
}

public sealed record DurableTimerHistoryQuery : RuntimeStorePageRequest
{
    public DurableTimerHistoryQuery(int limit = DefaultLimit, string? continuationToken = null) : base(limit, continuationToken) { }
}
