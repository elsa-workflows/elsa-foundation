using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Elsa.Serialization.Core;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

public enum EfDesignOperationStatus { Committed, Reconciled, Replayed, Conflict, Rejected }
public sealed record EfDesignOperationResult(EfDesignOperationStatus Status, string? ResultJson = null);

/// Provider-neutral atomic boundary owned by the EF adapter. The ledger row is inserted in the same
/// transaction as every W01-W05 mutation, making retries deterministic without exposing EF types.
public interface IDesignAtomicWriter
{
    Task<T> ExecuteAsync<T>(DesignOperationKey key, string operationKind, object request, Func<CancellationToken, Task<T>> stage, CancellationToken cancellationToken = default);
}

public sealed class EfDesignAtomicWriter(WorkflowsDesignDbContext db, TimeProvider? timeProvider = null) : IDesignAtomicWriter
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<T> ExecuteAsync<T>(DesignOperationKey key, string operationKind, object request, Func<CancellationToken, Task<T>> stage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stage);
        var requestFingerprint = EfDesignSupport.Fingerprint(operationKind, request);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await db.Operations.SingleOrDefaultAsync(x => x.OperationKind == operationKind && x.OperationKey == key.Value, cancellationToken);
        if (existing is not null)
        {
            if (!StringComparer.Ordinal.Equals(existing.RequestFingerprint, requestFingerprint))
                throw new InvalidOperationException($"Design operation '{operationKind}/{key.Value}' conflicts with an earlier request.");
            await transaction.CommitAsync(cancellationToken);
            return EfDesignSupport.ReadJson<T>(existing.ResultJson);
        }

        var value = await stage(cancellationToken);
        var resultJson = EfDesignSupport.Json(value);
        db.Operations.Add(new DesignOperationEntity
        {
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
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            var winner = await db.Operations.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKind == operationKind && x.OperationKey == key.Value, cancellationToken);
            if (winner is not null && winner.RequestFingerprint == requestFingerprint)
                return EfDesignSupport.ReadJson<T>(winner.ResultJson);
            throw;
        }
    }
}
