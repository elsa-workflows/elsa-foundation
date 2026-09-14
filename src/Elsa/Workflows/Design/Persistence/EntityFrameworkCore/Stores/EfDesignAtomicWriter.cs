using System.Text.Json;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Serialization.Core;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

public sealed class EfDesignAtomicWriter(
    WorkflowsDesignDbContext db,
    IPersistenceAccessContextAccessor access,
    TimeProvider? timeProvider = null,
    TimeSpan? reconciliationTimeout = null) : IDesignAtomicWriter
{
    private static readonly TimeSpan ReconciliationBackoff = TimeSpan.FromMilliseconds(25);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan timeout = reconciliationTimeout ?? TimeSpan.FromSeconds(10);

    public Task<T> ExecuteAsync<T>(
        DesignOperationKey key,
        string operationKind,
        object request,
        IReadOnlyCollection<string> mutatedUnits,
        Func<CancellationToken, Task<T>> stage,
        CancellationToken cancellationToken = default) =>
        DesignAtomicWriterExtensions.ExecuteAsync(this, key, operationKind, request, mutatedUnits, stage, cancellationToken);

    public Task<DesignAtomicWriteResult<T>> ExecuteAsync<T>(
        DesignOperationKey key,
        string operationKind,
        object request,
        IReadOnlyCollection<string> mutatedUnits,
        Func<IDesignAtomicWriteContext, CancellationToken, Task<DesignAtomicWriteStage<T>>> stage,
        Func<CancellationToken, Task>? beforeAttempt = null,
        CancellationToken cancellationToken = default,
        IDesignAtomicWriteResultCodec<T>? resultCodec = null)
    {
        ArgumentNullException.ThrowIfNull(mutatedUnits);
        if (mutatedUnits.Count == 0 || mutatedUnits.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A design operation must declare at least one mutated unit.", nameof(mutatedUnits));
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(reconciliationTimeout));
        resultCodec ??= new DefaultResultCodec<T>();
        return ExecuteAttemptAsync(key, operationKind, request, stage, beforeAttempt, cancellationToken, resultCodec, 0);
    }

    private async Task<DesignAtomicWriteResult<T>> ExecuteAttemptAsync<T>(
        DesignOperationKey key,
        string operationKind,
        object request,
        Func<IDesignAtomicWriteContext, CancellationToken, Task<DesignAtomicWriteStage<T>>> stage,
        Func<CancellationToken, Task>? beforeAttempt,
        CancellationToken cancellationToken,
        IDesignAtomicWriteResultCodec<T> resultCodec,
        int attempt)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stage);
        var tenantId = access.Current.Scope?.Value
                       ?? throw new InvalidOperationException("Workflow design mutations require an explicit persistence scope.");
        var requestFingerprint = EfDesignSupport.Fingerprint(operationKind, request);
        var legacyRequestFingerprint = EfDesignSupport.LegacyFingerprint(operationKind, request);
        var existing = await db.Operations.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.OperationKind == operationKind && x.OperationKey == key.Value,
            cancellationToken);
        if (existing is not null)
            return ResolveExisting(existing, operationKind, requestFingerprint, legacyRequestFingerprint, DesignAtomicWriteStatus.Replayed, resultCodec);
        if (attempt == 0 && beforeAttempt is not null)
            await beforeAttempt(cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        DesignAtomicWriteStage<T> staged;
        try
        {
            staged = await stage(EmptyContext.Instance, cancellationToken);
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
        ArgumentNullException.ThrowIfNull(staged);
        if (!staged.IsAccepted)
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            finally { db.ChangeTracker.Clear(); }
            return new DesignAtomicWriteResult<T>(DesignAtomicWriteStatus.Rejected, default);
        }

        var value = staged.Value!;
        var resultJson = staged.ResultJson ?? EfDesignSupport.Json(value);
        var resultFingerprint = staged.ResultFingerprint ?? EfDesignSupport.Fingerprint(operationKind + ".result", value);
        try
        {
            ValidateAuthoritativeResult(staged, value, operationKind, resultJson, resultFingerprint, resultCodec);
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
        db.Operations.Add(new DesignOperationEntity
        {
            TenantId = tenantId,
            OperationKind = operationKind,
            OperationKey = key.Value,
            RequestFingerprint = requestFingerprint,
            ResultFingerprint = resultFingerprint,
            ResultJson = resultJson,
            CreatedAt = clock.GetUtcNow()
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            try
            {
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await transaction.DisposeAsync();
                db.ChangeTracker.Clear();
                return await ReconcileAfterCommitAsync<T>(
                    tenantId, operationKind, key.Value, requestFingerprint, legacyRequestFingerprint, exception, resultCodec);
            }
            return new DesignAtomicWriteResult<T>(DesignAtomicWriteStatus.Committed, value, resultFingerprint, resultJson);
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
        {
            try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
            db.ChangeTracker.Clear();
            if (attempt >= 3)
                throw;
            await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1)), cancellationToken);
            await transaction.DisposeAsync();
            return await ExecuteAttemptAsync(key, operationKind, request, stage, beforeAttempt, cancellationToken, resultCodec, attempt + 1);
        }
        catch (DbUpdateException)
        {
            try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
            db.ChangeTracker.Clear();
            var winner = await db.Operations.AsNoTracking().SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.OperationKind == operationKind && x.OperationKey == key.Value,
                cancellationToken);
            if (winner is not null)
                return ResolveExisting(winner, operationKind, requestFingerprint, legacyRequestFingerprint, DesignAtomicWriteStatus.Reconciled, resultCodec);
            throw;
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<DesignAtomicWriteResult<T>> ReconcileAfterCommitAsync<T>(
        string tenantId,
        string operationKind,
        string operationKey,
        string requestFingerprint,
        string legacyRequestFingerprint,
        Exception commitException,
        IDesignAtomicWriteResultCodec<T> resultCodec)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        while (true)
        {
            db.ChangeTracker.Clear();
            try
            {
                var winner = await db.Operations.AsNoTracking().SingleOrDefaultAsync(
                    x => x.TenantId == tenantId && x.OperationKind == operationKind && x.OperationKey == operationKey,
                    timeoutSource.Token);
                if (winner is not null)
                    return ResolveExisting(winner, operationKind, requestFingerprint, legacyRequestFingerprint, DesignAtomicWriteStatus.Reconciled, resultCodec);
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                // A failed acknowledgement can leave the connection unavailable briefly. Keep
                // retrying the durable marker until the bounded recovery window expires.
                _ = exception;
            }
            try
            {
                await Task.Delay(ReconciliationBackoff, clock, timeoutSource.Token);
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
            {
                throw new DesignAtomicWriteUnknownOutcomeException(
                    $"The EF commit acknowledgement for design operation '{operationKind}/{operationKey}' has an unknown outcome after bounded recovery.",
                    commitException);
            }
        }
    }

    private static DesignAtomicWriteResult<T> ResolveExisting<T>(
        DesignOperationEntity existing,
        string operationKind,
        string requestFingerprint,
        string legacyRequestFingerprint,
        DesignAtomicWriteStatus matchingStatus,
        IDesignAtomicWriteResultCodec<T> resultCodec)
    {
        if (!StringComparer.Ordinal.Equals(existing.RequestFingerprint, requestFingerprint) &&
            !StringComparer.Ordinal.Equals(existing.RequestFingerprint, legacyRequestFingerprint))
            return new DesignAtomicWriteResult<T>(DesignAtomicWriteStatus.Conflict, default);
        var value = DeserializeResult(resultCodec, existing.ResultJson);
        if (!EfDesignSupport.IsResultFingerprintValid(operationKind + ".result", existing.ResultFingerprint, existing.ResultJson)
            && !StringComparer.Ordinal.Equals(existing.ResultFingerprint, EfDesignSupport.Fingerprint(operationKind + ".result", value)))
            throw new InvalidDataException("The authoritative design-operation result fingerprint does not match its payload.");
        return new DesignAtomicWriteResult<T>(matchingStatus, value, existing.ResultFingerprint, existing.ResultJson);
    }

    private static void ValidateAuthoritativeResult<T>(
        DesignAtomicWriteStage<T> staged,
        T value,
        string operationKind,
        string resultJson,
        string resultFingerprint,
        IDesignAtomicWriteResultCodec<T> resultCodec)
    {
        if ((staged.ResultFingerprint is null) != (staged.ResultJson is null))
            throw new InvalidDataException("An accepted design operation must provide both result fingerprint and result payload.");
        var suppliedValue = DeserializeResult(resultCodec, resultJson);
        if (!EfDesignSupport.IsResultFingerprintValid(operationKind + ".result", resultFingerprint, resultJson)
            && !StringComparer.Ordinal.Equals(resultFingerprint, EfDesignSupport.Fingerprint(operationKind + ".result", suppliedValue)))
            throw new InvalidDataException("The accepted design-operation result fingerprint does not match its payload.");
        if (!resultCodec.Equivalent(value, suppliedValue))
            throw new InvalidDataException("The accepted design-operation result must match its staged value.");
    }

    private static T DeserializeResult<T>(IDesignAtomicWriteResultCodec<T> resultCodec, string json)
    {
        try
        {
            return resultCodec.Deserialize(json);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("The authoritative design-operation result could not be deserialized.", exception);
        }
    }

    private sealed class DefaultResultCodec<T> : IDesignAtomicWriteResultCodec<T>
    {
        public T Deserialize(string json) => EfDesignSupport.ReadJson<T>(json);

        public bool Equivalent(T left, T right)
        {
            using var leftDocument = JsonDocument.Parse(EfDesignSupport.Json(left));
            using var rightDocument = JsonDocument.Parse(EfDesignSupport.Json(right));
            return JsonElement.DeepEquals(leftDocument.RootElement, rightDocument.RootElement);
        }
    }

    private sealed class EmptyContext : IDesignAtomicWriteContext
    {
        public static readonly EmptyContext Instance = new();
    }
}
