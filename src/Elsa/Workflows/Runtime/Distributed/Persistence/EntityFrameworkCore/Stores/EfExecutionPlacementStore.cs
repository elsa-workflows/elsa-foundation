using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// EF Core placement authority. The row's digest is the provider-neutral scoped key and the
/// explicit revision is the compare-and-swap token; no provider SQL or Groundwork API is used.
/// </summary>
public sealed class EfExecutionPlacementStore(
    ExecutionPlacementDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IExecutionPlacementStore
{
    private const int MaxCasAttempts = 8;

    public async ValueTask<ExecutionPlacementLease?> FindAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var id = PlacementIdentity.CreateId(scope, workflowExecutionId);
        try
        {
            var row = await context.PlacementLeases.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == id,
                cancellationToken);
            return row is null ? null : MapChecked(row, scope, workflowExecutionId, id);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsProviderException(exception))
        {
            throw NormalizeProviderFailure("finding", workflowExecutionId, exception);
        }
    }

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
        var id = PlacementIdentity.CreateId(scope, claim.WorkflowExecutionId);

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

                if (current is not null && current.ExpiresAt <= now && current.ExpiresAtUtcTicks > now.UtcTicks)
                    throw new InvalidOperationException("The placement row contains inconsistent expiry projections.");

                if (current is not null && current.ExpiresAtUtcTicks > now.UtcTicks &&
                    !StringComparer.Ordinal.Equals(current.OwnerId, claim.OwnerId))
                {
                    return new ExecutionPlacementClaimResult(
                        ExecutionPlacementClaimOutcome.Denied,
                        Map(current));
                }

                var outcome = current is not null &&
                              StringComparer.Ordinal.Equals(current.OwnerId, claim.OwnerId) &&
                              current.ExpiresAtUtcTicks > now.UtcTicks
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
                    current.OwnerIdHash = PlacementIdentity.CreateHash(lease.OwnerId);
                    current.PlacementToken = lease.PlacementToken;
                    current.AcquiredAt = lease.AcquiredAt;
                    current.ExpiresAt = lease.ExpiresAt;
                    current.ExpiresAtUtcTicks = lease.ExpiresAt.UtcTicks;
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
                if (attempt == MaxCasAttempts - 1)
                    throw ContentionFailure("claiming", claim.WorkflowExecutionId, exception);
            }
            catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception) || EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
            {
                context.ChangeTracker.Clear();
                if (attempt == MaxCasAttempts - 1)
                    throw ContentionFailure("claiming", claim.WorkflowExecutionId, exception);
            }
            catch (DbException exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
            {
                context.ChangeTracker.Clear();
                if (attempt == MaxCasAttempts - 1)
                    throw ContentionFailure("claiming", claim.WorkflowExecutionId, exception);
            }
            catch (Exception exception) when (IsProviderException(exception))
            {
                context.ChangeTracker.Clear();
                throw NormalizeProviderFailure("claiming", claim.WorkflowExecutionId, exception);
            }
        }

        throw new InvalidOperationException("The placement claim did not settle after bounded compare-and-swap retries.");
    }

    public async ValueTask ReleaseAsync(
        ExecutionPlacementLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        DistributedRuntimeIdentityConstraints.Validate(lease.WorkflowExecutionId, nameof(lease.WorkflowExecutionId));
        DistributedRuntimeIdentityConstraints.Validate(lease.OwnerId, nameof(lease.OwnerId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var id = PlacementIdentity.CreateId(scope, lease.WorkflowExecutionId);

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
                if (!StringComparer.Ordinal.Equals(current.OwnerId, lease.OwnerId) ||
                    current.PlacementToken != lease.PlacementToken)
                    return;

                context.PlacementLeases.Remove(current);
                await context.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (OperationCanceledException)
            {
                context.ChangeTracker.Clear();
                throw;
            }
            catch (DbUpdateConcurrencyException)
            {
                context.ChangeTracker.Clear();
                if (attempt == MaxCasAttempts - 1)
                    return;
            }
            catch (DbException exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
            {
                context.ChangeTracker.Clear();
                if (attempt == MaxCasAttempts - 1)
                    return;
            }
            catch (Exception exception) when (IsProviderException(exception))
            {
                context.ChangeTracker.Clear();
                throw NormalizeProviderFailure("releasing", lease.WorkflowExecutionId, exception);
            }
        }
    }

    public async ValueTask<IReadOnlyList<ExecutionPlacementLease>> ListOwnedAsync(
        ExecutionPlacementLeaseListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DistributedRuntimeIdentityConstraints.Validate(request.OwnerId, nameof(request.OwnerId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var scopeHash = PlacementIdentity.CreateHash(scope);
        var ownerHash = PlacementIdentity.CreateHash(request.OwnerId);

        try
        {
            var rows = await context.PlacementLeases.AsNoTracking()
                .Where(row => row.ScopeKeyHash == scopeHash &&
                              row.OwnerIdHash == ownerHash &&
                              row.ExpiresAtUtcTicks > request.Now.UtcTicks)
                .OrderBy(row => row.ExpiresAtUtcTicks)
                .ThenBy(row => row.WorkflowExecutionIdOrderKey)
                .ThenBy(row => row.Id)
                .Take(request.Take)
                .ToListAsync(cancellationToken);

            return rows
                .Select(row => MapChecked(row, scope, row.WorkflowExecutionId, row.Id))
                .Where(lease => StringComparer.Ordinal.Equals(lease.OwnerId, request.OwnerId) && !lease.IsExpired(request.Now))
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsProviderException(exception))
        {
            throw NormalizeProviderFailure("listing", scope, exception);
        }
    }

    private string RequireScope() => accessContextAccessor.Current.Scope?.Value ??
        throw new InvalidOperationException("EF distributed stores require a scoped persistence access context.");

    private static ExecutionPlacementLease MapChecked(
        ExecutionPlacementLeaseEntity row,
        string scope,
        string workflowExecutionId,
        string id)
    {
        EnsureIdentity(row, scope, workflowExecutionId, id);
        return Map(row);
    }

    private static void EnsureIdentity(ExecutionPlacementLeaseEntity row, string scope, string workflowExecutionId, string id)
    {
        if (!StringComparer.Ordinal.Equals(row.Id, id) ||
            !StringComparer.Ordinal.Equals(row.ScopeKey, scope) ||
            !StringComparer.Ordinal.Equals(row.WorkflowExecutionId, workflowExecutionId))
            throw new InvalidOperationException("The placement identity digest maps to a different scope or execution identity.");
    }

    private static ExecutionPlacementLease Map(ExecutionPlacementLeaseEntity row) =>
        new(row.WorkflowExecutionId, row.OwnerId, row.PlacementToken, row.AcquiredAt, row.ExpiresAt);

    private static ExecutionPlacementLeaseEntity ToEntity(ExecutionPlacementLease lease, string scope, string id, long revision) => new()
    {
        Id = id,
        ScopeKey = scope,
        ScopeKeyHash = PlacementIdentity.CreateHash(scope),
        WorkflowExecutionId = lease.WorkflowExecutionId,
        WorkflowExecutionIdOrderKey = PlacementIdentity.CreateOrderKey(lease.WorkflowExecutionId),
        OwnerId = lease.OwnerId,
        OwnerIdHash = PlacementIdentity.CreateHash(lease.OwnerId),
        PlacementToken = lease.PlacementToken,
        AcquiredAt = lease.AcquiredAt,
        ExpiresAt = lease.ExpiresAt,
        ExpiresAtUtcTicks = lease.ExpiresAt.UtcTicks,
        Revision = revision
    };

    private static bool IsProviderException(Exception exception) =>
        exception is DbException or DbUpdateException or DbUpdateConcurrencyException;

    private static InvalidOperationException NormalizeProviderFailure(string operation, string identity, Exception inner) =>
        new($"The EF execution placement store failed while {operation} placement '{identity}'.", inner);

    private static InvalidOperationException ContentionFailure(string operation, string identity, Exception inner) =>
        new($"The EF execution placement store could not complete {operation} placement '{identity}' after {MaxCasAttempts} bounded compare-and-swap attempts.", inner);

    private static class PlacementIdentity
    {
        public static string CreateId(string scope, string executionId) =>
            CreateHash($"{scope.Length}:{scope}{executionId.Length}:{executionId}");

        public static string CreateHash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        public static string CreateOrderKey(string value)
        {
            var builder = new StringBuilder(value.Length * 4);
            foreach (var character in value)
                builder.Append(((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }
}
