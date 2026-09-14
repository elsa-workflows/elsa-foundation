using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Serialization.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

public sealed class EfDesignAtomicWriter(
    WorkflowsDesignDbContext db,
    IPersistenceAccessContextAccessor access,
    TimeProvider? timeProvider = null,
    TimeSpan? reconciliationTimeout = null,
    Func<CancellationToken, Task<IDbContextTransaction>>? transactionFactory = null) : IDesignAtomicWriter
{
    private static readonly TimeSpan ReconciliationBackoff = TimeSpan.FromMilliseconds(25);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan timeout = reconciliationTimeout ?? TimeSpan.FromSeconds(10);
    private readonly Func<CancellationToken, Task<IDbContextTransaction>> beginTransaction = transactionFactory ?? (ct => db.Database.BeginTransactionAsync(ct));

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
        EfDesignSupport.ValidateOperationIdentity(key, operationKind);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stage);
        var tenantId = access.Current.Scope?.Value
                       ?? throw new InvalidOperationException("Workflow design mutations require an explicit persistence scope.");
        string requestFingerprint;
        string legacyRequestFingerprint;
        try
        {
            requestFingerprint = EfDesignSupport.Fingerprint(operationKind, request);
            legacyRequestFingerprint = EfDesignSupport.LegacyFingerprint(operationKind, request);
        }
        catch (DesignPersistenceException) { throw; }
        catch (Exception exception) when (IsSerializationFailure(exception))
        {
            throw SerializationFailure(operationKind, exception);
        }
        var existing = await EfDesignSupport.ReadAsync("reading design operation marker", () => db.Operations.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.OperationKind == operationKind && x.OperationKey == key.Value,
            cancellationToken));
        if (existing is not null)
            return ResolveExisting(existing, operationKind, requestFingerprint, legacyRequestFingerprint, DesignAtomicWriteStatus.Replayed, resultCodec);
        // Attempt setup is deliberately rerun after every transient write conflict. Providers can
        // invalidate locks, snapshots, and other preflight observations while a transaction is
        // being retried; reusing the first attempt's setup would silently weaken those guarantees.
        if (beforeAttempt is not null)
            await beforeAttempt(cancellationToken);

        var transaction = await EfDesignSupport.ReadAsync("starting design operation transaction", () => beginTransaction(cancellationToken));
        var transactionDisposed = false;
        DesignAtomicWriteStage<T> staged;
        try
        {
            staged = await EfDesignSupport.ReadAsync("staging design operation", () => stage(EmptyContext.Instance, cancellationToken));
        }
        catch (Exception exception)
        {
            db.ChangeTracker.Clear();
            transactionDisposed = true;
            await CleanupAsync(transaction, exception, operationKind, rollback: true);
            throw;
        }
        try
        {
            ArgumentNullException.ThrowIfNull(staged);
        }
        catch (Exception exception)
        {
            transactionDisposed = true;
            await CleanupAsync(transaction, exception, operationKind, rollback: true);
            db.ChangeTracker.Clear();
            throw;
        }
        if (!staged.IsAccepted)
        {
            transactionDisposed = true;
            await CleanupAsync(transaction, null, operationKind, rollback: true);
            db.ChangeTracker.Clear();
            return new DesignAtomicWriteResult<T>(DesignAtomicWriteStatus.Rejected, default);
        }

        var value = staged.Value!;
        string resultJson;
        string resultFingerprint;
        try
        {
            resultJson = staged.ResultJson ?? EfDesignSupport.Json(value);
            resultFingerprint = staged.ResultFingerprint ?? EfDesignSupport.Fingerprint(operationKind + ".result", value);
            ValidateAuthoritativeResult(staged, value, operationKind, resultJson, resultFingerprint, resultCodec);
        }
        catch (DesignPersistenceException exception)
        {
            transactionDisposed = true;
            await CleanupAsync(transaction, exception, operationKind, rollback: true);
            db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (IsSerializationFailure(exception))
        {
            var failure = SerializationFailure(operationKind, exception);
            transactionDisposed = true;
            await CleanupAsync(transaction, failure, operationKind, rollback: true);
            db.ChangeTracker.Clear();
            throw failure;
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
                transactionDisposed = true;
                await CleanupAsync(transaction, exception, operationKind, rollback: false);
                db.ChangeTracker.Clear();
                return await ReconcileAfterCommitAsync<T>(
                    tenantId, operationKind, key.Value, requestFingerprint, legacyRequestFingerprint, exception, resultCodec);
            }
            return new DesignAtomicWriteResult<T>(DesignAtomicWriteStatus.Committed, value, resultFingerprint, resultJson);
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
        {
            transactionDisposed = true;
            await CleanupAsync(transaction, exception, operationKind, rollback: true);
            db.ChangeTracker.Clear();
            if (attempt >= 3)
                throw ProviderFailure(operationKind, exception);
            await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1)), cancellationToken);
            return await ExecuteAttemptAsync(key, operationKind, request, stage, beforeAttempt, cancellationToken, resultCodec, attempt + 1);
        }
        catch (DbUpdateException exception)
        {
            transactionDisposed = true;
            await CleanupAsync(transaction, exception, operationKind, rollback: true);
            db.ChangeTracker.Clear();
            var winner = await EfDesignSupport.ReadAsync("reading design operation winner", () => db.Operations.AsNoTracking().SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.OperationKind == operationKind && x.OperationKey == key.Value,
                cancellationToken));
            if (winner is not null)
                // The local stage lost the marker race. The concurrent winner owns publication;
                // classify this caller as a replay so it cannot emit duplicate post-commit events.
                return ResolveExisting(winner, operationKind, requestFingerprint, legacyRequestFingerprint, DesignAtomicWriteStatus.Replayed, resultCodec);
            throw ProviderFailure(operationKind, exception);
        }
        catch (Exception exception)
        {
            transactionDisposed = true;
            await CleanupAsync(transaction, exception, operationKind, rollback: true);
            db.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            if (!transactionDisposed)
                await CleanupAsync(transaction, null, operationKind, rollback: false);
        }
    }

    private static async Task CleanupAsync(IDbContextTransaction transaction, Exception? primary, string operationKind, bool rollback)
    {
        var cleanupFailures = new List<Exception>();
        if (rollback)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }
        }

        try
        {
            await transaction.DisposeAsync();
        }
        catch (Exception exception)
        {
            cleanupFailures.Add(exception);
        }

        if (cleanupFailures.Count == 0)
            return;

        if (primary is null)
            ExceptionDispatchInfo.Capture(cleanupFailures.Count == 1 ? cleanupFailures[0] : new AggregateException(cleanupFailures)).Throw();

        // Cleanup is diagnostic only once the operation already has an authoritative failure.
        // In particular, provider cancellation during rollback/disposal must not mask it.
        foreach (var (failure, index) in cleanupFailures.Select((failure, index) => (failure, index)))
        foreach (var exception in ExceptionChain(primary))
            exception.Data[$"Elsa.Design.Atomic.CleanupFailure.{operationKind}.{index}"] = failure;
        Trace.TraceWarning("Workflow design transaction cleanup failed for {0}: {1}", operationKind, string.Join("; ", cleanupFailures.Select(failure => failure.Message)));
    }

    private static IEnumerable<Exception> ExceptionChain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            yield return current;
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
                var winner = await EfDesignSupport.ReadAsync("reconciling design operation marker", () => db.Operations.AsNoTracking().SingleOrDefaultAsync(
                    x => x.TenantId == tenantId && x.OperationKind == operationKind && x.OperationKey == operationKey,
                    timeoutSource.Token));
                if (winner is not null)
                    return ResolveExisting(winner, operationKind, requestFingerprint, legacyRequestFingerprint, DesignAtomicWriteStatus.Reconciled, resultCodec);
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
            {
                throw new DesignAtomicWriteUnknownOutcomeException(
                    $"The EF commit acknowledgement for design operation '{operationKind}/{operationKey}' has an unknown outcome after bounded recovery.",
                    commitException);
            }
            catch (DesignPersistenceException exception) when (exception.FailureKind == DesignPersistenceFailureKind.Provider)
            {
                // A provider can briefly reject reads while the commit acknowledgement is
                // being recovered. Keep the bounded reconciliation window authoritative;
                // do not expose the wrapped provider exception as a terminal result.
                _ = exception;
            }
            catch (DesignPersistenceException) { throw; }
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
            throw SerializationFailure(operationKind, new InvalidDataException("The authoritative design-operation result fingerprint does not match its payload."));
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
            throw SerializationFailure(operationKind, new InvalidDataException("An accepted design operation must provide both result fingerprint and result payload."));
        var suppliedValue = DeserializeResult(resultCodec, resultJson);
        if (!EfDesignSupport.IsResultFingerprintValid(operationKind + ".result", resultFingerprint, resultJson)
            && !StringComparer.Ordinal.Equals(resultFingerprint, EfDesignSupport.Fingerprint(operationKind + ".result", suppliedValue)))
            throw SerializationFailure(operationKind, new InvalidDataException("The accepted design-operation result fingerprint does not match its payload."));
        if (!resultCodec.Equivalent(value, suppliedValue))
            throw SerializationFailure(operationKind, new InvalidDataException("The accepted design-operation result must match its staged value."));
    }

    private static T DeserializeResult<T>(IDesignAtomicWriteResultCodec<T> resultCodec, string json)
    {
        try
        {
            return resultCodec.Deserialize(json);
        }
        catch (InvalidDataException exception)
        {
            throw SerializationFailure("design-operation.result", exception);
        }
        catch (Exception exception) when (IsSerializationFailure(exception))
        {
            throw SerializationFailure("design-operation.result", exception);
        }
    }

    private static bool IsSerializationFailure(Exception exception) =>
        exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException or AccessViolationException);

    private static DesignPersistenceException ProviderFailure(string operation, Exception exception) =>
        new(DesignPersistenceDomain.Workflow, DesignPersistenceFailureKind.Provider, operation, null, exception.InnerException ?? exception);

    private static DesignPersistenceException SerializationFailure(string operation, Exception exception) =>
        new(DesignPersistenceDomain.Workflow, DesignPersistenceFailureKind.Serialization, operation, "design operation", exception);

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
