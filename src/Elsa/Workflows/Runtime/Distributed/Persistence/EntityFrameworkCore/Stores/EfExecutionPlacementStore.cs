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
            return row is null ? null : MapChecked(row, scope, workflowExecutionId);
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
                    current.OwnerIdHash = PlacementIdentity.CreateHash(lease.OwnerId);
                    current.PlacementToken = lease.PlacementToken;
                    current.AcquiredAt = lease.AcquiredAt;
                    current.ExpiresAtUtcTicks = lease.ExpiresAt.UtcTicks;
                    current.ExpiresAtOffsetMinutes = checked((int)lease.ExpiresAt.Offset.TotalMinutes);
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
            catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
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
        var encodedScope = PlacementIdentity.EncodeScope(scope);
        var scopeHash = PlacementIdentity.CreateHash(scope);
        var ownerHash = PlacementIdentity.CreateHash(request.OwnerId);

        try
        {
            var rows = await context.PlacementLeases.AsNoTracking()
                .Where(row => row.ScopeKeyHash == scopeHash &&
                              row.OwnerIdHash == ownerHash &&
                              row.ScopeKey == encodedScope &&
                              row.OwnerId == request.OwnerId &&
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
        string? expectedWorkflowExecutionId = null,
        string? expectedOwnerId = null)
    {
        if (expectedWorkflowExecutionId is not null &&
            !StringComparer.Ordinal.Equals(row.WorkflowExecutionId, expectedWorkflowExecutionId))
            throw new InvalidOperationException("The placement identity digest maps to a different execution identity.");

        if (expectedOwnerId is not null &&
            !StringComparer.Ordinal.Equals(row.OwnerId, expectedOwnerId))
            throw new InvalidOperationException("The placement row belongs to a different owner.");

        var id = PlacementIdentity.CreateId(scope, row.WorkflowExecutionId);
        EnsureIdentity(row, scope, row.WorkflowExecutionId, id);
        return Map(row);
    }

    private static void EnsureIdentity(ExecutionPlacementLeaseEntity row, string scope, string workflowExecutionId, string id)
    {
        if (!StringComparer.Ordinal.Equals(row.Id, id) ||
            !StringComparer.Ordinal.Equals(PlacementIdentity.DecodeScope(row.ScopeKey), scope) ||
            !StringComparer.Ordinal.Equals(row.WorkflowExecutionId, workflowExecutionId))
            throw new InvalidOperationException("The placement identity digest maps to a different scope or execution identity.");

        if (!StringComparer.Ordinal.Equals(row.ScopeKeyHash, PlacementIdentity.CreateHash(scope)) ||
            !StringComparer.Ordinal.Equals(row.OwnerIdHash, PlacementIdentity.CreateHash(row.OwnerId)) ||
            !StringComparer.Ordinal.Equals(row.WorkflowExecutionIdOrderKey, PlacementIdentity.CreateOrderKey(row.WorkflowExecutionId)))
            throw new InvalidOperationException("The placement row contains inconsistent derived projections.");

        _ = ReadExpiresAt(row);
    }

    private static bool IsLive(ExecutionPlacementLeaseEntity row, DateTimeOffset now) =>
        ReadExpiresAt(row) > now;

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
        ScopeKey = PlacementIdentity.EncodeScope(scope),
        ScopeKeyHash = PlacementIdentity.CreateHash(scope),
        WorkflowExecutionId = lease.WorkflowExecutionId,
        WorkflowExecutionIdOrderKey = PlacementIdentity.CreateOrderKey(lease.WorkflowExecutionId),
        OwnerId = lease.OwnerId,
        OwnerIdHash = PlacementIdentity.CreateHash(lease.OwnerId),
        PlacementToken = lease.PlacementToken,
        AcquiredAt = lease.AcquiredAt,
        ExpiresAtUtcTicks = lease.ExpiresAt.UtcTicks,
        ExpiresAtOffsetMinutes = checked((int)lease.ExpiresAt.Offset.TotalMinutes),
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

        public static string CreateHash(string value)
        {
            return Convert.ToHexString(SHA256.HashData(EncodeUtf16CodeUnits(value)));
        }

        public static string EncodeScope(string value)
            => Convert.ToBase64String(EncodeUtf16CodeUnits(value));

        private static byte[] EncodeUtf16CodeUnits(string value)
        {
            var bytes = new byte[checked(value.Length * sizeof(char))];
            for (var index = 0; index < value.Length; index++)
            {
                var codeUnit = value[index];
                bytes[index * sizeof(char)] = (byte)codeUnit;
                bytes[index * sizeof(char) + 1] = (byte)(codeUnit >> 8);
            }

            return bytes;
        }

        public static string DecodeScope(string encoded)
        {
            try
            {
                var bytes = Convert.FromBase64String(encoded);
                if (bytes.Length % sizeof(char) != 0)
                    throw new FormatException("The encoded scope does not contain complete UTF-16 code units.");

                var chars = new char[bytes.Length / sizeof(char)];
                for (var index = 0; index < chars.Length; index++)
                    chars[index] = (char)(bytes[index * sizeof(char)] | bytes[index * sizeof(char) + 1] << 8);
                return new string(chars);
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException)
            {
                throw new InvalidOperationException("The placement row contains an invalid encoded scope projection.", exception);
            }
        }

        public static string CreateOrderKey(string value)
        {
            var builder = new StringBuilder(value.Length * 4);
            foreach (var character in value)
                builder.Append(((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }
}
