using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Elsa.Serialization.Core;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

public enum EfDesignOperationStatus { Committed, Reconciled, Replayed, Conflict, Rejected }
public sealed record EfDesignOperationResult(EfDesignOperationStatus Status, string? ResultJson = null);

public sealed class EfDesignAtomicWriter(WorkflowsDesignDbContext db, TimeProvider? timeProvider = null, IPersistenceAccessContextAccessor? access = null) : IDesignAtomicWriter
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<T> ExecuteAsync<T>(DesignOperationKey key, string operationKind, object request, Func<CancellationToken, Task<T>> stage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stage);
        var tenantId = access?.Current.Scope?.Value ?? string.Empty;
        if (access is not null && access.Current.Scope is null)
            throw new InvalidOperationException("Workflow design mutations require an explicit persistence scope.");
        var requestFingerprint = EfDesignSupport.Fingerprint(operationKind, request);
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
            return EfDesignSupport.ReadJson<T>(existing.ResultJson);
        }

        var value = await stage(cancellationToken);
        var resultJson = EfDesignSupport.Json(value);
        db.Operations.Add(new DesignOperationEntity
        {
            TenantId = tenantId,
            OperationKind = operationKind,
            OperationKey = key.Value,
            RequestFingerprint = requestFingerprint,
            ResultFingerprint = EfDesignSupport.Fingerprint(operationKind + ".result", value),
            ResultJson = resultJson,
            CreatedAt = clock.GetUtcNow()
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return value;
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
        {
            db.ChangeTracker.Clear();
            throw;
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
                return EfDesignSupport.ReadJson<T>(winner.ResultJson);
            }
            throw;
        }
    }
}
