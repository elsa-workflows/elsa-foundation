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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutatedUnits);
        if (mutatedUnits.Count == 0 || mutatedUnits.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A design operation must declare at least one mutated unit.", nameof(mutatedUnits));
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(reconciliationTimeout));
        return ExecuteAttemptAsync(key, operationKind, request, stage, beforeAttempt, cancellationToken, 0);
    }

    private async Task<DesignAtomicWriteResult<T>> ExecuteAttemptAsync<T>(
        DesignOperationKey key,
        string operationKind,
        object request,
        Func<IDesignAtomicWriteContext, CancellationToken, Task<DesignAtomicWriteStage<T>>> stage,
        Func<CancellationToken, Task>? beforeAttempt,
        CancellationToken cancellationToken,
        int attempt)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stage);
        var tenantId = access.Current.Scope?.Value
                       ?? throw new InvalidOperationException("Workflow design mutations require an explicit persistence scope.");
        var requestFingerprint = EfDesignSupport.Fingerprint(operationKind, request);
        var existing = await db.Operations.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.OperationKind == operationKind && x.OperationKey == key.Value,
            cancellationToken);
        if (existing is not null)
            return ResolveExisting<T>(existing, operationKind, requestFingerprint, DesignAtomicWriteStatus.Replayed);
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
            ValidateAuthoritativeResult(staged, value, operationKind, resultJson, resultFingerprint);
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
                    tenantId, operationKind, key.Value, requestFingerprint, exception);
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
            return await ExecuteAttemptAsync(key, operationKind, request, stage, beforeAttempt, cancellationToken, attempt + 1);
        }
        catch (DbUpdateException)
        {
            try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
            db.ChangeTracker.Clear();
            var winner = await db.Operations.AsNoTracking().SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.OperationKind == operationKind && x.OperationKey == key.Value,
                cancellationToken);
            if (winner is not null)
                return ResolveExisting<T>(winner, operationKind, requestFingerprint, DesignAtomicWriteStatus.Reconciled);
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
        Exception commitException)
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
                    return ResolveExisting<T>(winner, operationKind, requestFingerprint, DesignAtomicWriteStatus.Reconciled);
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
        DesignAtomicWriteStatus matchingStatus)
    {
        if (!StringComparer.Ordinal.Equals(existing.RequestFingerprint, requestFingerprint))
            return new DesignAtomicWriteResult<T>(DesignAtomicWriteStatus.Conflict, default);
        var value = EfDesignSupport.ReadJson<T>(existing.ResultJson);
        var expectedResultFingerprint = EfDesignSupport.Fingerprint(operationKind + ".result", value);
        if (!StringComparer.Ordinal.Equals(existing.ResultFingerprint, expectedResultFingerprint))
            throw new InvalidDataException("The authoritative design-operation result fingerprint does not match its payload.");
        return new DesignAtomicWriteResult<T>(matchingStatus, value, existing.ResultFingerprint, existing.ResultJson);
    }

    private static void ValidateAuthoritativeResult<T>(
        DesignAtomicWriteStage<T> staged,
        T value,
        string operationKind,
        string resultJson,
        string resultFingerprint)
    {
        if ((staged.ResultFingerprint is null) != (staged.ResultJson is null))
            throw new InvalidDataException("An accepted design operation must provide both result fingerprint and result payload.");
        var expectedJson = EfDesignSupport.Json(value);
        var expectedFingerprint = EfDesignSupport.Fingerprint(operationKind + ".result", value);
        if (!StringComparer.Ordinal.Equals(resultFingerprint, expectedFingerprint) || !JsonEquivalent(resultJson, expectedJson))
            throw new InvalidDataException("The accepted design-operation result must match its staged value.");
    }

    private static bool JsonEquivalent(string left, string right)
    {
        using var leftDocument = JsonDocument.Parse(left);
        using var rightDocument = JsonDocument.Parse(right);
        return JsonElement.DeepEquals(leftDocument.RootElement, rightDocument.RootElement);
    }

    private sealed class EmptyContext : IDesignAtomicWriteContext
    {
        public static readonly EmptyContext Instance = new();
    }
}
