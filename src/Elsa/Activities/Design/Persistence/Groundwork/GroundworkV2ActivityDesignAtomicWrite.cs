using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.Core.Design;

namespace Elsa.Activities.Design.Persistence.Groundwork;

/// <summary>Public-v2 atomic writer for activity-design operations and their replay marker.</summary>
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

public sealed class GroundworkDesignAtomicWrite(GroundworkV2ActivityDesignStore store) : IDesignAtomicWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<GroundworkDesignAtomicWriteResult> ExecuteAsync(
        GroundworkDesignAtomicWriteRequest request,
        Func<GroundworkDesignAtomicWriteContext, CancellationToken, Task<GroundworkDesignAtomicWriteStageResult>> stage,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(request, null, stage, cancellationToken);

    public async Task<GroundworkDesignAtomicWriteResult> ExecuteAsync(
        GroundworkDesignAtomicWriteRequest request,
        Func<CancellationToken, Task>? beforeAttempt,
        Func<GroundworkDesignAtomicWriteContext, CancellationToken, Task<GroundworkDesignAtomicWriteStageResult>> stage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stage);
        cancellationToken.ThrowIfCancellationRequested();

        var markerId = MarkerId(request.Operation);
        var existing = await store.LoadAsync(
            ActivitiesDesignStorageManifest.DesignOperationDocumentKind, markerId, cancellationToken);
        if (existing is not null)
            return Resolve(existing, request);

        if (beforeAttempt is not null)
            await beforeAttempt(cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ExecuteAttemptAsync(request, markerId, stage, cancellationToken);
            }
            catch (ActivityDesignWriteConflictException) when (attempt < 4)
            {
                // A create-only marker conflict may be observed before the winner is durable.
                // Retry the exact operation a bounded number of times without re-running the
                // caller's preflight callback.
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
                var winner = await store.LoadAsync(
                    ActivitiesDesignStorageManifest.DesignOperationDocumentKind,
                    markerId,
                    cancellationToken);
                if (winner is not null)
                    return Resolve(winner, request);
            }
            catch (ActivityDesignWriteConflictException)
            {
                var winner = await store.LoadAsync(
                    ActivitiesDesignStorageManifest.DesignOperationDocumentKind,
                    markerId,
                    cancellationToken);
                if (winner is not null)
                    return Resolve(winner, request);

                throw;
            }
        }
    }

    private async Task<GroundworkDesignAtomicWriteResult> ExecuteAttemptAsync(
        GroundworkDesignAtomicWriteRequest request,
        string markerId,
        Func<GroundworkDesignAtomicWriteContext, CancellationToken, Task<GroundworkDesignAtomicWriteStageResult>> stage,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = store.Begin(new ActivityDesignCommitScope(
            request.MutatedDocumentKinds.Append(ActivitiesDesignStorageManifest.DesignOperationDocumentKind).ToArray()));
        var context = new GroundworkDesignAtomicWriteContext(unitOfWork);
        var staged = await stage(context, cancellationToken);
        ArgumentNullException.ThrowIfNull(staged);
        if (!staged.IsAccepted)
        {
            unitOfWork.Rollback();
            return GroundworkDesignAtomicWriteResult.Rejected();
        }

        var marker = new DesignOperationMarker(
            request.Operation.OperationKind,
            request.Operation.OperationKey,
            request.CanonicalRequestFingerprint,
            staged.AuthoritativeResultFingerprint!,
            staged.AuthoritativeResultJson!);
        await context.SaveAsync(new ActivityDesignSaveRequest(
            ActivitiesDesignStorageManifest.DesignOperationDocumentKind,
            markerId,
            ActivitiesDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(marker, JsonOptions),
            ExpectedVersion: 0), cancellationToken);
        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (ActivityDesignWriteConflictException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A provider may acknowledge the commit only after the durable transaction has
            // completed. The marker is the authoritative classification for that ambiguity;
            // do not stage the mutation a second time when it is already durable.
            try
            {
                var winner = await store.LoadAsync(
                    ActivitiesDesignStorageManifest.DesignOperationDocumentKind,
                    markerId,
                    CancellationToken.None);
                if (winner is not null)
                    return ResolveReconciled(winner, request);
            }
            catch
            {
                // Preserve the provider's original failure when reconciliation cannot classify it.
            }

            throw;
        }
        return GroundworkDesignAtomicWriteResult.Committed(
            staged.AuthoritativeResultFingerprint!, staged.AuthoritativeResultJson!);
    }

    private static GroundworkDesignAtomicWriteResult Resolve(
        ActivityDesignDocument markerDocument,
        GroundworkDesignAtomicWriteRequest request)
    {
        var marker = JsonSerializer.Deserialize<DesignOperationMarker>(markerDocument.ContentJson, JsonOptions)
                     ?? throw new InvalidDataException("The design operation marker is unreadable.");
        if (!StringComparer.Ordinal.Equals(marker.OperationKind, request.Operation.OperationKind) ||
            !StringComparer.Ordinal.Equals(marker.OperationKey, request.Operation.OperationKey))
            throw new InvalidDataException("The design operation marker identity does not match its key.");
        if (!StringComparer.Ordinal.Equals(marker.CanonicalRequestFingerprint, request.CanonicalRequestFingerprint))
            return GroundworkDesignAtomicWriteResult.Conflict();
        return GroundworkDesignAtomicWriteResult.Replayed(
            marker.AuthoritativeResultFingerprint, marker.AuthoritativeResultJson);
    }

    private static GroundworkDesignAtomicWriteResult ResolveReconciled(
        ActivityDesignDocument markerDocument,
        GroundworkDesignAtomicWriteRequest request)
    {
        var result = Resolve(markerDocument, request);
        return result.Status == GroundworkDesignAtomicWriteStatus.Replayed
            ? result with { Status = GroundworkDesignAtomicWriteStatus.Reconciled }
            : result;
    }

    private static string MarkerId(GroundworkDesignOperationIdentity operation)
    {
        var material = $"elsa-design-operation:v2|{operation.OperationKind.Length}:{operation.OperationKind}|{operation.OperationKey.Length}:{operation.OperationKey}";
        return $"design-operation-v2-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))}";
    }

    private sealed record DesignOperationMarker(
        string OperationKind,
        string OperationKey,
        string CanonicalRequestFingerprint,
        string AuthoritativeResultFingerprint,
        string AuthoritativeResultJson);
}

public sealed record GroundworkDesignOperationIdentity
{
    public GroundworkDesignOperationIdentity(string operationKind, string operationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);
        OperationKind = operationKind;
        OperationKey = operationKey;
    }

    public string OperationKind { get; }
    public string OperationKey { get; }
}

public sealed record GroundworkDesignAtomicWriteRequest
{
    public GroundworkDesignAtomicWriteRequest(
        GroundworkDesignOperationIdentity operation,
        string canonicalRequestFingerprint,
        IReadOnlyCollection<string> mutatedDocumentKinds)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalRequestFingerprint);
        ArgumentNullException.ThrowIfNull(mutatedDocumentKinds);
        if (mutatedDocumentKinds.Count == 0 || mutatedDocumentKinds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one non-empty mutated document kind is required.", nameof(mutatedDocumentKinds));
        Operation = operation;
        CanonicalRequestFingerprint = canonicalRequestFingerprint;
        MutatedDocumentKinds = mutatedDocumentKinds.Distinct(StringComparer.Ordinal).ToArray();
    }

    public GroundworkDesignOperationIdentity Operation { get; }
    public string CanonicalRequestFingerprint { get; }
    public IReadOnlyCollection<string> MutatedDocumentKinds { get; }
}

public sealed class GroundworkDesignAtomicWriteContext(ActivityDesignUnitOfWork unitOfWork)
{
    public async Task<ActivityDesignWriteResult> SaveAsync(
        ActivityDesignSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        unitOfWork.StageSave(request);
        return new ActivityDesignWriteResult(ActivityDesignWriteStatus.Saved);
    }

    public Task<ActivityDesignWriteResult> DeleteAsync(
        ActivityDesignDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        unitOfWork.StageDelete(request);
        return Task.FromResult(new ActivityDesignWriteResult(ActivityDesignWriteStatus.Deleted));
    }

    public Task<ActivityDesignDocument?> LoadAsync(
        string documentKind,
        string id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(unitOfWork.Load(documentKind, id));
    }
}

public enum ActivityDesignWriteStatus { Saved, Deleted, Conflict }

public sealed record ActivityDesignWriteResult(ActivityDesignWriteStatus Status);

public sealed record GroundworkDesignAtomicWriteStageResult(
    bool IsAccepted,
    string? AuthoritativeResultFingerprint,
    string? AuthoritativeResultJson)
{
    public static GroundworkDesignAtomicWriteStageResult Accepted(string fingerprint, string json) =>
        new(true, fingerprint, json);

    public static GroundworkDesignAtomicWriteStageResult Rejected() => new(false, null, null);
}

public enum GroundworkDesignAtomicWriteStatus
{
    Committed,
    Reconciled,
    Replayed,
    Rejected,
    Conflict
}

public sealed record GroundworkDesignAtomicWriteResult(
    GroundworkDesignAtomicWriteStatus Status,
    string? AuthoritativeResultFingerprint,
    string? AuthoritativeResultJson)
{
    public static GroundworkDesignAtomicWriteResult Committed(string fingerprint, string json) =>
        new(GroundworkDesignAtomicWriteStatus.Committed, fingerprint, json);

    public static GroundworkDesignAtomicWriteResult Replayed(string fingerprint, string json) =>
        new(GroundworkDesignAtomicWriteStatus.Replayed, fingerprint, json);

    public static GroundworkDesignAtomicWriteResult Conflict() =>
        new(GroundworkDesignAtomicWriteStatus.Conflict, null, null);

    public static GroundworkDesignAtomicWriteResult Rejected() =>
        new(GroundworkDesignAtomicWriteStatus.Rejected, null, null);
}

public static class GroundworkDesignAtomicCommand
{
    private const string MaterialSchemaVersion = "2";

    public static async Task<GroundworkDesignAtomicCommandResult<TResult>> ExecuteAsync<TRequest, TResult>(
        IDesignAtomicWriter atomicWrite,
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

        var options = jsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var requestJson = JsonSerializer.Serialize(
            new { Kind = operationKind, Version = MaterialSchemaVersion, Value = requestMaterial }, options);
        var requestFingerprint = Fingerprint(requestJson);
        var result = await atomicWrite.ExecuteAsync(
            new(
                new GroundworkDesignOperationIdentity(operationKind, operationKey.Value),
                requestFingerprint,
                mutatedDocumentKinds),
            beforeAttempt,
            async (context, token) =>
            {
                var value = await stage(context, token);
                ArgumentNullException.ThrowIfNull(value);
                var json = JsonSerializer.Serialize(value, options);
                return GroundworkDesignAtomicWriteStageResult.Accepted(Fingerprint(json), json);
            },
            cancellationToken);

        return result.Status switch
        {
            GroundworkDesignAtomicWriteStatus.Committed or
                GroundworkDesignAtomicWriteStatus.Reconciled or
                GroundworkDesignAtomicWriteStatus.Replayed =>
                new(
                    JsonSerializer.Deserialize<TResult>(result.AuthoritativeResultJson!, options)
                    ?? throw new InvalidDataException("The authoritative design operation result is unreadable."),
                    result.Status),
            GroundworkDesignAtomicWriteStatus.Conflict =>
                throw new GroundworkDesignOperationConflictException(operationKind, operationKey.Value),
            GroundworkDesignAtomicWriteStatus.Rejected =>
                throw new GroundworkDesignOperationRejectedException(operationKind, operationKey.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(result.Status))
        };
    }

    private static string Fingerprint(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}

public sealed record GroundworkDesignAtomicCommandResult<TResult>(
    TResult Value,
    GroundworkDesignAtomicWriteStatus Status)
    where TResult : notnull
{
    public bool ShouldPublishPostCommitOutcome =>
        Status is GroundworkDesignAtomicWriteStatus.Committed or GroundworkDesignAtomicWriteStatus.Reconciled;
}

public sealed class GroundworkDesignOperationConflictException(string operationKind, string operationKey)
    : InvalidOperationException($"Design operation key '{operationKey}' is already bound to different material for '{operationKind}'.")
{
    public string OperationKind { get; } = operationKind;
    public string OperationKey { get; } = operationKey;
}

public sealed class GroundworkDesignOperationRejectedException(string operationKind, string operationKey)
    : InvalidOperationException($"Groundwork rejected design operation '{operationKind}' with key '{operationKey}' and rolled it back.")
{
    public string OperationKind { get; } = operationKind;
    public string OperationKey { get; } = operationKey;
}
