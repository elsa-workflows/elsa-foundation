using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// EF Core placement authority. The row's digest is the provider-neutral scoped key and the
/// explicit revision is the compare-and-swap token; no provider SQL is used.
/// </summary>
public sealed class EfExecutionPlacementStore(
    ExecutionPlacementDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IExecutionPlacementStore
{
    private const int MaxCasAttempts = 8;

    /// <inheritdoc/>
    /// <exception cref="ExecutionPlacementEntityFrameworkPersistenceException">The EF provider cannot complete the lookup.</exception>
    public async ValueTask<ExecutionPlacementLease?> FindAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var id = EfDistributedIdentity.CreateId(scope, workflowExecutionId);
        try
        {
            var row = await context.PlacementLeases.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == id,
                cancellationToken);
            if (row is null)
                return null;

            var lease = MapChecked(row, scope, workflowExecutionId);
            return row.IsReleased ? null : lease;
        }
        catch (OperationCanceledException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (IsPersistenceBoundaryFailure(exception))
        {
            context.ChangeTracker.Clear();
            throw NormalizeProviderFailure("finding", workflowExecutionId, exception);
        }
    }

    /// <inheritdoc/>
    /// <exception cref="ExecutionPlacementEntityFrameworkPersistenceException">The EF provider cannot complete the claim or bounded contention does not settle.</exception>
    public async ValueTask<ExecutionPlacementClaimResult> TryClaimAsync(
        ExecutionPlacementClaim claim,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        DistributedRuntimeIdentityConstraints.Validate(claim.WorkflowExecutionId, nameof(claim.WorkflowExecutionId));
        DistributedRuntimeIdentityConstraints.Validate(claim.OwnerId, nameof(claim.OwnerId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var id = EfDistributedIdentity.CreateId(scope, claim.WorkflowExecutionId);

        Exception lastContention = null!;
        for (var attempt = 0; attempt < MaxCasAttempts; attempt++)
        {
            context.ChangeTracker.Clear();
            try
            {
                var current = await context.PlacementLeases.SingleOrDefaultAsync(
                    row => row.Id == id,
                    cancellationToken);
                if (current is not null)
                    EnsureIdentity(current, scope, claim.WorkflowExecutionId, id);

                var isLive = current is not null && IsLive(current, now);

                if (current is not null && isLive &&
                    !StringComparer.Ordinal.Equals(current.OwnerId, claim.OwnerId))
                {
                    return new ExecutionPlacementClaimResult(
                        ExecutionPlacementClaimOutcome.Denied,
                        Map(current));
                }

                var outcome = current is not null &&
                              StringComparer.Ordinal.Equals(current.OwnerId, claim.OwnerId) &&
                              isLive
                    ? ExecutionPlacementClaimOutcome.Renewed
                    : ExecutionPlacementClaimOutcome.Granted;
                var lease = new ExecutionPlacementLease(
                    claim.WorkflowExecutionId,
                    claim.OwnerId,
                    checked((current?.PlacementToken ?? 0) + 1),
                    claim.RequestedAt,
                    claim.ExpiresAt);

                if (current is null)
                {
                    context.PlacementLeases.Add(ToEntity(lease, scope, id, revision: 1));
                }
                else
                {
                    current.OwnerId = lease.OwnerId;
                    current.OwnerIdHash = EfDistributedIdentity.Hash(lease.OwnerId);
                    current.PlacementToken = lease.PlacementToken;
                    current.AcquiredAt = lease.AcquiredAt;
                    current.ExpiresAtUtcTicks = lease.ExpiresAt.UtcTicks;
                    current.ExpiresAtOffsetMinutes = checked((int)lease.ExpiresAt.Offset.TotalMinutes);
                    current.IsReleased = false;
                    current.Revision = checked(current.Revision + 1);
                }

                await context.SaveChangesAsync(cancellationToken);
                return new ExecutionPlacementClaimResult(outcome, lease);
            }
            catch (OperationCanceledException)
            {
                context.ChangeTracker.Clear();
                throw;
            }
            catch (DbUpdateConcurrencyException exception)
            {
                context.ChangeTracker.Clear();
                lastContention = exception;
            }
            catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception) || EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
            {
                context.ChangeTracker.Clear();
                lastContention = exception;
            }
            catch (DbException exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
            {
                context.ChangeTracker.Clear();
                lastContention = exception;
            }
            catch (Exception exception) when (IsPersistenceBoundaryFailure(exception))
            {
                context.ChangeTracker.Clear();
                throw NormalizeProviderFailure("claiming", claim.WorkflowExecutionId, exception);
            }
        }

        throw ContentionFailure("claiming", claim.WorkflowExecutionId, lastContention);
    }

    /// <inheritdoc/>
    /// <exception cref="ExecutionPlacementEntityFrameworkPersistenceException">The EF provider cannot complete the release or bounded contention does not settle.</exception>
    public async ValueTask ReleaseAsync(
        ExecutionPlacementLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        DistributedRuntimeIdentityConstraints.Validate(lease.WorkflowExecutionId, nameof(lease.WorkflowExecutionId));
        DistributedRuntimeIdentityConstraints.Validate(lease.OwnerId, nameof(lease.OwnerId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var id = EfDistributedIdentity.CreateId(scope, lease.WorkflowExecutionId);

        Exception lastContention = null!;
        for (var attempt = 0; attempt < MaxCasAttempts; attempt++)
        {
            context.ChangeTracker.Clear();
            try
            {
                var current = await context.PlacementLeases.SingleOrDefaultAsync(
                    row => row.Id == id,
                    cancellationToken);
                if (current is null)
                    return;
                EnsureIdentity(current, scope, lease.WorkflowExecutionId, id);
                if (current.IsReleased ||
                    !StringComparer.Ordinal.Equals(current.OwnerId, lease.OwnerId) ||
                    current.PlacementToken != lease.PlacementToken)
                    return;

                current.IsReleased = true;
                current.Revision = checked(current.Revision + 1);
                await context.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (OperationCanceledException)
            {
                context.ChangeTracker.Clear();
                throw;
            }
            catch (DbUpdateConcurrencyException exception)
            {
                context.ChangeTracker.Clear();
                lastContention = exception;
            }
            catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
            {
                context.ChangeTracker.Clear();
                lastContention = exception;
            }
            catch (DbException exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
            {
                context.ChangeTracker.Clear();
                lastContention = exception;
            }
            catch (Exception exception) when (IsPersistenceBoundaryFailure(exception))
            {
                context.ChangeTracker.Clear();
                throw NormalizeProviderFailure("releasing", lease.WorkflowExecutionId, exception);
            }
        }

        throw ContentionFailure("releasing", lease.WorkflowExecutionId, lastContention);
    }

    /// <inheritdoc/>
    /// <exception cref="ExecutionPlacementEntityFrameworkPersistenceException">The EF provider cannot complete the bounded lookup.</exception>
    public async ValueTask<IReadOnlyList<ExecutionPlacementLease>> ListOwnedAsync(
        ExecutionPlacementLeaseListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DistributedRuntimeIdentityConstraints.Validate(request.OwnerId, nameof(request.OwnerId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var encodedScope = EfDistributedIdentity.EncodeScope(scope);
        var scopeHash = EfDistributedIdentity.Hash(scope);
        var ownerHash = EfDistributedIdentity.Hash(request.OwnerId);

        try
        {
            var rows = await context.PlacementLeases.AsNoTracking()
                .Where(row => row.ScopeKeyHash == scopeHash &&
                              row.OwnerIdHash == ownerHash &&
                              row.ScopeKey == encodedScope &&
                              row.OwnerId == request.OwnerId &&
                              !row.IsReleased &&
                              row.ExpiresAtUtcTicks > request.Now.UtcTicks)
                .OrderBy(row => row.ExpiresAtUtcTicks)
                .ThenBy(row => row.WorkflowExecutionIdOrderKey)
                .ThenBy(row => row.Id)
                .Take(request.Take)
                .ToListAsync(cancellationToken);

            return rows.Select(row =>
            {
                var lease = MapChecked(row, scope, expectedOwnerId: request.OwnerId);
                _ = IsLive(row, request.Now);
                return lease;
            }).ToArray();
        }
        catch (OperationCanceledException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (IsPersistenceBoundaryFailure(exception))
        {
            context.ChangeTracker.Clear();
            throw NormalizeProviderFailure("listing", $"scope:{scopeHash}", exception);
        }
    }

    private string RequireScope() => accessContextAccessor.Current.Scope?.Value ??
        throw new InvalidOperationException("EF distributed stores require a scoped persistence access context.");

    private static ExecutionPlacementLease MapChecked(
        ExecutionPlacementLeaseEntity row,
        string scope,
        string? expectedWorkflowExecutionId = null,
        string? expectedOwnerId = null)
    {
        if (expectedWorkflowExecutionId is not null &&
            !StringComparer.Ordinal.Equals(row.WorkflowExecutionId, expectedWorkflowExecutionId))
            throw new InvalidOperationException("The placement identity digest maps to a different execution identity.");

        if (expectedOwnerId is not null &&
            !StringComparer.Ordinal.Equals(row.OwnerId, expectedOwnerId))
            throw new InvalidOperationException("The placement row belongs to a different owner.");

        var id = EfDistributedIdentity.CreateId(scope, row.WorkflowExecutionId);
        EnsureIdentity(row, scope, row.WorkflowExecutionId, id);
        return Map(row);
    }

    private static void EnsureIdentity(ExecutionPlacementLeaseEntity row, string scope, string workflowExecutionId, string id)
    {
        if (!StringComparer.Ordinal.Equals(row.Id, id) ||
            !StringComparer.Ordinal.Equals(EfDistributedIdentity.DecodeScope(row.ScopeKey), scope) ||
            !StringComparer.Ordinal.Equals(row.WorkflowExecutionId, workflowExecutionId))
            throw new InvalidOperationException("The placement identity digest maps to a different scope or execution identity.");

        if (!StringComparer.Ordinal.Equals(row.ScopeKeyHash, EfDistributedIdentity.Hash(scope)) ||
            !StringComparer.Ordinal.Equals(row.OwnerIdHash, EfDistributedIdentity.Hash(row.OwnerId)) ||
            !row.WorkflowExecutionIdOrderKey.AsSpan().SequenceEqual(EfDistributedIdentity.CreateOrderKey(row.WorkflowExecutionId, ExecutionPlacementEfModule.WorkflowExecutionIdOrderKeyWidth)))
            throw new InvalidOperationException("The placement row contains inconsistent derived projections.");

        var expiresAt = ReadExpiresAt(row);
        if (row.PlacementToken <= 0 || row.Revision <= 0 || expiresAt <= row.AcquiredAt)
            throw new InvalidOperationException("The placement row contains invalid lease state.");
    }

    private static bool IsLive(ExecutionPlacementLeaseEntity row, DateTimeOffset now) =>
        !row.IsReleased && ReadExpiresAt(row) > now;

    private static DateTimeOffset ReadExpiresAt(ExecutionPlacementLeaseEntity row)
    {
        if (row.ExpiresAtOffsetMinutes is < -14 * 60 or > 14 * 60)
            throw new InvalidOperationException("The placement row contains an invalid expiry offset.");

        try
        {
            return new DateTimeOffset(row.ExpiresAtUtcTicks, TimeSpan.Zero)
                .ToOffset(TimeSpan.FromMinutes(row.ExpiresAtOffsetMinutes));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("The placement row contains an invalid expiry value.", exception);
        }
    }

    private static ExecutionPlacementLease Map(ExecutionPlacementLeaseEntity row) =>
        new(row.WorkflowExecutionId, row.OwnerId, row.PlacementToken, row.AcquiredAt, ReadExpiresAt(row));

    private static ExecutionPlacementLeaseEntity ToEntity(ExecutionPlacementLease lease, string scope, string id, long revision) => new()
    {
        Id = id,
        ScopeKey = EfDistributedIdentity.EncodeScope(scope),
        ScopeKeyHash = EfDistributedIdentity.Hash(scope),
        WorkflowExecutionId = lease.WorkflowExecutionId,
        WorkflowExecutionIdOrderKey = EfDistributedIdentity.CreateOrderKey(lease.WorkflowExecutionId, ExecutionPlacementEfModule.WorkflowExecutionIdOrderKeyWidth),
        OwnerId = lease.OwnerId,
        OwnerIdHash = EfDistributedIdentity.Hash(lease.OwnerId),
        PlacementToken = lease.PlacementToken,
        AcquiredAt = lease.AcquiredAt,
        ExpiresAtUtcTicks = lease.ExpiresAt.UtcTicks,
        ExpiresAtOffsetMinutes = checked((int)lease.ExpiresAt.Offset.TotalMinutes),
        IsReleased = false,
        Revision = revision
    };

    private static ExecutionPlacementEntityFrameworkPersistenceException NormalizeProviderFailure(
        string operation,
        string identity,
        Exception inner) =>
        new(operation, identity, $"The EF execution placement store failed while {operation} placement '{identity}'.", inner);

    private static bool IsPersistenceBoundaryFailure(Exception exception) =>
        exception is DbException or DbUpdateException or InvalidOperationException or ArgumentException or OverflowException;

    private static ExecutionPlacementEntityFrameworkPersistenceException ContentionFailure(
        string operation,
        string identity,
        Exception inner) =>
        new(operation, identity, $"The EF execution placement store could not complete {operation} placement '{identity}' after {MaxCasAttempts} bounded compare-and-swap attempts.", inner);

}
