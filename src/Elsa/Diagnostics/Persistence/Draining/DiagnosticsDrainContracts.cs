using Elsa.Diagnostics.Persistence.Observability;

namespace Elsa.Diagnostics.Persistence.Draining;

/// <summary>A stable identity reused for every attempt to commit one drain batch.</summary>
public readonly record struct DiagnosticsDrainBatchId(Guid Value)
{
    public static DiagnosticsDrainBatchId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

/// <summary>The bounded unit supplied to a concrete diagnostics persistence adapter.</summary>
public sealed record DiagnosticsDrainBatch<TItem>(
    DiagnosticsDrainBatchId Id,
    IReadOnlyList<TItem> Items) where TItem : notnull;

/// <summary>An authoritative commit outcome in the same order as the accepted batch.</summary>
public sealed record DiagnosticsDrainCommit<TResult>(
    IReadOnlyList<TResult> Results,
    int RetentionUnits);

/// <summary>
/// Replacement contract: exactly one target is composed per drain. Implementations keep provider
/// operations and operation-id binding in their concrete persistence project.
/// </summary>
public interface IDiagnosticsDrainTarget<TItem, TResult> where TItem : notnull
{
    ValueTask<DiagnosticsDrainCommit<TResult>> CommitAsync(
        DiagnosticsDrainBatch<TItem> batch,
        CancellationToken cancellationToken = default);

    ValueTask<int> ApplyRetentionAsync(CancellationToken cancellationToken = default);
}

public sealed record DiagnosticsDrainStopResult(
    DiagnosticsDrainState State,
    bool Drained);

/// <summary>A caller-visible failure for an accepted or rejected diagnostics capture.</summary>
public sealed class DiagnosticsDrainException : Exception
{
    public DiagnosticsDrainException(
        DiagnosticsPersistenceLossReason reason,
        string message,
        Exception? innerException = null) : base(message, innerException)
    {
        Reason = reason;
    }

    public DiagnosticsPersistenceLossReason Reason { get; }
}
