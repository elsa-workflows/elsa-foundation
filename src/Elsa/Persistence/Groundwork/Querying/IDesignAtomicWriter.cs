namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Replacement contract for replay-safe, multi-document design mutations over Groundwork.
/// </summary>
public interface IDesignAtomicWriter
{
    Task<GroundworkDesignAtomicWriteResult> ExecuteAsync(
        GroundworkDesignAtomicWriteRequest request,
        Func<GroundworkDesignAtomicWriteContext, CancellationToken, Task<GroundworkDesignAtomicWriteStageResult>> stage,
        CancellationToken cancellationToken = default);

    Task<GroundworkDesignAtomicWriteResult> ExecuteAsync(
        GroundworkDesignAtomicWriteRequest request,
        Func<CancellationToken, Task>? beforeAttempt,
        Func<GroundworkDesignAtomicWriteContext, CancellationToken, Task<GroundworkDesignAtomicWriteStageResult>> stage,
        CancellationToken cancellationToken = default);
}
