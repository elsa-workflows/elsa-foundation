using System.Text.Json;
using Elsa.Persistence.Core.Design;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Command-facing adapter over <see cref="GroundworkDesignAtomicWrite"/>. It keeps canonical request
/// material separate from caller operation identity, persists a typed authoritative result, and maps
/// terminal storage decisions to stable Groundwork command exceptions.
/// </summary>
public static class GroundworkDesignAtomicCommand
{
    private const string MaterialSchemaVersion = "1";

    public static async Task<GroundworkDesignAtomicCommandResult<TResult>> ExecuteAsync<TRequest, TResult>(
        GroundworkDesignAtomicWrite atomicWrite,
        DesignOperationKey operationKey,
        string operationKind,
        TRequest requestMaterial,
        IReadOnlyCollection<string> mutatedDocumentKinds,
        Func<GroundworkDesignAtomicWriteContext, CancellationToken, Task<TResult>> stage,
        JsonSerializerOptions? jsonOptions = null,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, Task>? beforeAttempt = null,
        DesignPersistenceDomain? persistenceDomain = null,
        string? failureContext = null)
        where TRequest : notnull
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(atomicWrite);
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        ArgumentNullException.ThrowIfNull(requestMaterial);
        ArgumentNullException.ThrowIfNull(mutatedDocumentKinds);
        ArgumentNullException.ThrowIfNull(stage);
        var effectiveDomain = persistenceDomain ?? ResolveDesignDomain(operationKind);

        try
        {
            var canonicalRequest = GroundworkDesignAtomicWriteMaterial.Create(
                operationKind,
                MaterialSchemaVersion,
                requestMaterial,
                jsonOptions);
            var result = await atomicWrite.ExecuteAsync(
                new GroundworkDesignAtomicWriteRequest(
                    new GroundworkDesignOperationIdentity(operationKind, operationKey.Value),
                    canonicalRequest.Fingerprint,
                    mutatedDocumentKinds),
                beforeAttempt,
                async (context, token) =>
                {
                    var value = await stage(context, token);
                    if (value is null)
                    {
                        throw new InvalidOperationException(
                            $"Design operation '{operationKind}' produced a null authoritative result.");
                    }

                    var authoritative = GroundworkDesignAtomicWriteMaterial.Create(
                        ResultKind(operationKind),
                        MaterialSchemaVersion,
                        value,
                        jsonOptions);
                    return GroundworkDesignAtomicWriteStageResult.Accepted(
                        authoritative.Fingerprint,
                        authoritative.Json);
                },
                cancellationToken);

            return result.Status switch
            {
                GroundworkDesignAtomicWriteStatus.Committed or
                    GroundworkDesignAtomicWriteStatus.Reconciled or
                    GroundworkDesignAtomicWriteStatus.Replayed =>
                    new GroundworkDesignAtomicCommandResult<TResult>(
                        GroundworkDesignAtomicWriteMaterial.Deserialize<TResult>(
                            result.AuthoritativeResultFingerprint!,
                            result.AuthoritativeResultJson!,
                            ResultKind(operationKind),
                            MaterialSchemaVersion,
                            jsonOptions),
                        result.Status),
                GroundworkDesignAtomicWriteStatus.Conflict =>
                    throw new GroundworkDesignOperationConflictException(
                        operationKind,
                        operationKey.Value),
                GroundworkDesignAtomicWriteStatus.Rejected =>
                    throw new GroundworkDesignOperationRejectedException(
                        operationKind,
                        operationKey.Value),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(result.Status),
                    result.Status,
                    "Unknown design atomic-write status.")
            };
        }
        catch (Exception exception) when (GroundworkDesignPersistenceBoundary.TryMap(
                   exception,
                   effectiveDomain,
                   operationKind,
                   failureContext,
                   out var mapped))
        {
            throw mapped!;
        }
    }

    private static string ResultKind(string operationKind) => $"{operationKind}.result";

    private static DesignPersistenceDomain? ResolveDesignDomain(string operationKind) =>
        operationKind.StartsWith("workflow.", StringComparison.Ordinal)
            ? DesignPersistenceDomain.Workflow
            : operationKind.StartsWith("activity.", StringComparison.Ordinal)
                ? DesignPersistenceDomain.Activity
                : null;
}

public sealed record GroundworkDesignAtomicCommandResult<TResult>(
    TResult Value,
    GroundworkDesignAtomicWriteStatus Status)
    where TResult : notnull
{
    public bool ShouldPublishPostCommitOutcome =>
        Status is GroundworkDesignAtomicWriteStatus.Committed or GroundworkDesignAtomicWriteStatus.Reconciled;
}

public sealed class GroundworkDesignOperationConflictException(
    string operationKind,
    string operationKey)
    : InvalidOperationException(
        $"Design operation key '{operationKey}' is already bound to different material for '{operationKind}'.")
{
    public string OperationKind { get; } = operationKind;
    public string OperationKey { get; } = operationKey;
}

public sealed class GroundworkDesignOperationRejectedException(
    string operationKind,
    string operationKey)
    : InvalidOperationException(
        $"Groundwork rejected design operation '{operationKind}' with key '{operationKey}' and rolled it back.")
{
    public string OperationKind { get; } = operationKind;
    public string OperationKey { get; } = operationKey;
}
