using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Elsa.Serialization.Core;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

public sealed class EfDesignAtomicWriter(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, TimeProvider? timeProvider = null) : IDesignAtomicWriter
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public Task<T> ExecuteAsync<T>(
        DesignOperationKey key,
        string operationKind,
        object request,
        Func<CancellationToken, Task<T>> stage,
        CancellationToken cancellationToken = default) =>
        DesignAtomicWriterExtensions.ExecuteAsync(this, key, operationKind, request, stage, cancellationToken);

    public Task<DesignAtomicWriteResult<T>> ExecuteAsync<T>(
        DesignOperationKey key,
        string operationKind,
        object request,
        IReadOnlyCollection<string> mutatedUnits,
        Func<IDesignAtomicWriteContext, CancellationToken, Task<DesignAtomicWriteStage<T>>> stage,
        Func<CancellationToken, Task>? beforeAttempt = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAttemptAsync(key, operationKind, request, stage, beforeAttempt, cancellationToken, 0);

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
        var tenantId = access.Current.Scope?.Value ?? throw new InvalidOperationException("Workflow design mutations require an explicit persistence scope.");
        if (access.Current.Scope is null)
            throw new InvalidOperationException("Workflow design mutations require an explicit persistence scope.");
        var requestFingerprint = EfDesignSupport.Fingerprint(operationKind, request);
        if (attempt == 0 && beforeAttempt is not null)
            await beforeAttempt(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await db.Operations.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.OperationKind == operationKind && x.OperationKey == key.Value, cancellationToken);
        if (existing is not null)
        {
            if (!StringComparer.Ordinal.Equals(existing.RequestFingerprint, requestFingerprint))
                throw new InvalidOperationException($"Design operation '{operationKind}/{key.Value}' conflicts with an earlier request.");
            var expectedResultFingerprint = EfDesignSupport.Fingerprint(operationKind + ".result", EfDesignSupport.ReadJson<T>(existing.ResultJson));
            if (!StringComparer.Ordinal.Equals(existing.ResultFingerprint, expectedResultFingerprint))
                throw new InvalidDataException("The authoritative design-operation result fingerprint does not match its payload.");
            await transaction.CommitAsync(cancellationToken);
            return new DesignAtomicWriteResult<T>(DesignAtomicWriteStatus.Replayed, EfDesignSupport.ReadJson<T>(existing.ResultJson), existing.ResultFingerprint, existing.ResultJson);
        }

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
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
            return new DesignAtomicWriteResult<T>(DesignAtomicWriteStatus.Rejected, default);
        }
        var value = staged.Value!;
        var resultJson = staged.ResultJson ?? EfDesignSupport.Json(value);
        var resultFingerprint = staged.ResultFingerprint ?? EfDesignSupport.Fingerprint(operationKind + ".result", value);
        if ((staged.ResultFingerprint is null) != (staged.ResultJson is null))
            throw new InvalidDataException("An accepted design operation must provide both result fingerprint and result payload.");
        if (staged.ResultFingerprint is not null &&
            !StringComparer.Ordinal.Equals(resultFingerprint, EfDesignSupport.Fingerprint(operationKind + ".result", EfDesignSupport.ReadJson<T>(resultJson))))
            throw new InvalidDataException("The accepted design-operation result fingerprint does not match its payload.");
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
            await transaction.CommitAsync(cancellationToken);
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
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            var winner = await db.Operations.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.OperationKind == operationKind && x.OperationKey == key.Value, cancellationToken);
            if (winner is not null && winner.RequestFingerprint == requestFingerprint)
            {
                var expectedResultFingerprint = EfDesignSupport.Fingerprint(operationKind + ".result", EfDesignSupport.ReadJson<T>(winner.ResultJson));
                if (!StringComparer.Ordinal.Equals(winner.ResultFingerprint, expectedResultFingerprint))
                    throw new InvalidDataException("The authoritative design-operation result fingerprint does not match its payload.");
                return new DesignAtomicWriteResult<T>(DesignAtomicWriteStatus.Reconciled, EfDesignSupport.ReadJson<T>(winner.ResultJson), winner.ResultFingerprint, winner.ResultJson);
            }
            throw;
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private sealed class EmptyContext : IDesignAtomicWriteContext
    {
        public static readonly EmptyContext Instance = new();
    }
}
