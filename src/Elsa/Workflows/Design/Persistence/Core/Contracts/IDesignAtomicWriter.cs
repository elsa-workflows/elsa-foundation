using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Replacement contract. Provider-neutral atomic boundary for design mutations. Exactly one
/// implementation is selected per application; use an explicit DI replacement operation when
/// customizing it. Implementations persist the operation
/// marker in the same transaction as staged changes and must make replay, conflict, and result
/// integrity decisions before invoking volatile source reads.
/// </summary>
public interface IDesignAtomicWriter
{
    Task<DesignAtomicWriteResult<T>> ExecuteAsync<T>(
        DesignOperationKey operationKey,
        string operationKind,
        object requestMaterial,
        IReadOnlyCollection<string> mutatedUnits,
        Func<IDesignAtomicWriteContext, CancellationToken, Task<DesignAtomicWriteStage<T>>> stage,
        Func<CancellationToken, Task>? beforeAttempt = null,
        CancellationToken cancellationToken = default,
        IDesignAtomicWriteResultCodec<T>? resultCodec = null);
}

/// <summary>
/// Provider-neutral result material semantics for atomic-write marker validation and replay.
/// Implementations may supply their own serialization policy without exposing a serializer
/// dependency in the Core contract.
/// </summary>
public interface IDesignAtomicWriteResultCodec<T>
{
    T Deserialize(string json);

    bool Equivalent(T left, T right);
}

public interface IDesignAtomicWriteContext
{
}

public enum DesignAtomicWriteStatus
{
    Committed,
    Reconciled,
    Replayed,
    Conflict,
    Rejected
}

public sealed record DesignAtomicWriteStage<T>(
    bool IsAccepted,
    T? Value,
    string? ResultFingerprint = null,
    string? ResultJson = null)
{
    public static DesignAtomicWriteStage<T> Accepted(T value) => new(true, value);
    public static DesignAtomicWriteStage<T> Accepted(T value, string resultFingerprint, string resultJson) =>
        new(true, value, resultFingerprint, resultJson);
    public static DesignAtomicWriteStage<T> Rejected() => new(false, default);
}

public sealed record DesignAtomicWriteResult<T>(
    DesignAtomicWriteStatus Status,
    T? Value,
    string? ResultFingerprint = null,
    string? ResultJson = null)
{
    public bool ShouldPublishPostCommitOutcome =>
        Status is DesignAtomicWriteStatus.Committed or DesignAtomicWriteStatus.Reconciled;
}

public static class DesignAtomicWriterExtensions
{
    public static async Task<T> ExecuteAsync<T>(
        this IDesignAtomicWriter writer,
        DesignOperationKey operationKey,
        string operationKind,
        object requestMaterial,
        IReadOnlyCollection<string> mutatedUnits,
        Func<CancellationToken, Task<T>> stage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(mutatedUnits);
        if (mutatedUnits.Count == 0 || mutatedUnits.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A design operation must declare at least one mutated unit.", nameof(mutatedUnits));
        ArgumentNullException.ThrowIfNull(stage);
        var result = await writer.ExecuteAsync(
            operationKey,
            operationKind,
            requestMaterial,
            mutatedUnits,
            async (_, token) => DesignAtomicWriteStage<T>.Accepted(await stage(token)),
            cancellationToken: cancellationToken);

        return result.Status switch
        {
            DesignAtomicWriteStatus.Committed or DesignAtomicWriteStatus.Reconciled or DesignAtomicWriteStatus.Replayed
                => result.Value!,
            DesignAtomicWriteStatus.Conflict
                => throw new InvalidOperationException($"Design operation '{operationKind}/{operationKey.Value}' conflicts with an earlier request."),
            DesignAtomicWriteStatus.Rejected
                => throw new InvalidOperationException($"Design operation '{operationKind}/{operationKey.Value}' was rejected."),
            _ => throw new ArgumentOutOfRangeException(nameof(result.Status))
        };
    }
}
