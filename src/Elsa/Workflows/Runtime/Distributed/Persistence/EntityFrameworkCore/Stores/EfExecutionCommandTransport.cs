using System.Data;
using System.Data.Common;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// EF Core implementation of the D02 stream head and D03 command transport. Both entity sets share this one
/// context and every mutation uses one relational transaction, so a head projection can never commit separately from
/// its command row.
/// </summary>
public sealed class EfExecutionCommandTransport(
    ExecutionCommandTransportDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IExecutionCommandTransport
{
    private const int MaxCasAttempts = 16;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<ExecutionCommandTransportItem> SendAsync(
        string workflowExecutionId,
        WorkflowExecutionCommandEnvelope envelope,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        ArgumentNullException.ThrowIfNull(envelope);
        if (!StringComparer.Ordinal.Equals(workflowExecutionId, envelope.WorkflowExecutionId))
            throw new ArgumentException("The command envelope workflow execution ID must match the requested execution ID.", nameof(envelope));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        EnsurePartition(envelope, scope);
        var scopeHash = EfDistributedIdentity.Hash(scope);
        var workflowHash = EfDistributedIdentity.Hash(workflowExecutionId);

        Exception? lastContention = null;
        for (var attempt = 0; attempt < MaxCasAttempts; attempt++)
        {
            context.ChangeTracker.Clear();
            try
            {
                await using var transaction = await BeginConsistencyTransactionAsync(cancellationToken);
                var head = await FindHeadAsync(scope, workflowExecutionId, cancellationToken);
                if (head is not null)
                {
                    EnsureHead(head, scope, workflowExecutionId);
                    await ValidateHeadStateAsync(head, scopeHash, workflowHash, scope, workflowExecutionId, cancellationToken);
                }
                else if (await HasItemsAsync(scopeHash, workflowHash, scope, workflowExecutionId, cancellationToken))
                    throw new InvalidOperationException("Command transport items exist without their stream head.");

                var sequence = checked((head?.LastSequence ?? 0) + 1);
                var item = new ExecutionCommandTransportItem(
                    ComposeTransportItemId(workflowExecutionId, sequence),
                    workflowExecutionId,
                    envelope,
                    sequence,
                    now);
                var itemEntity = ToEntity(item, scope, scopeHash, workflowHash);
                var pendingCount = head?.PendingCount ?? 0;
                if (pendingCount < 0)
                    throw new InvalidOperationException("The command stream head has a negative pending count.");
                if (head is null)
                {
                    context.CommandStreamHeads.Add(new ExecutionCommandStreamHeadEntity
                    {
                        Id = EfDistributedIdentity.CreateId(scope, workflowExecutionId),
                        ScopeKey = EfDistributedIdentity.EncodeScope(scope),
                        ScopeKeyHash = scopeHash,
                        WorkflowExecutionId = workflowExecutionId,
                        WorkflowExecutionIdHash = workflowHash,
                        WorkflowExecutionIdOrderKey = EfDistributedIdentity.CreateOrderKey(workflowExecutionId, ExecutionCommandTransportEfModule.WorkflowExecutionIdOrderKeyWidth),
                        LastSequence = sequence,
                        PendingCount = 1,
                        PendingVisibleAtUtcTicks = 0,
                        PendingSequence = sequence,
                        Revision = 1
                    });
                }
                else
                {
                    head.LastSequence = sequence;
                    head.PendingCount = checked(pendingCount + 1);
                    var previousEarliestWasVisible = pendingCount > 0 && head.PendingVisibleAtUtcTicks == 0;
                    head.PendingVisibleAtUtcTicks = 0;
                    // A newly appended item is unleased and therefore visible at tick zero. It becomes the
                    // first pending item when the stream was empty, the earliest-visible item when every older
                    // item is leased, or follows the earlier sequence among an already-visible prefix.
                    head.PendingSequence = pendingCount == 0
                        ? sequence
                        : previousEarliestWasVisible
                        ? Math.Min(head.PendingSequence, sequence)
                        : sequence;
                    head.Revision = checked(head.Revision + 1);
                }

                context.CommandTransportItems.Add(itemEntity);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return item;
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
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
            {
                context.ChangeTracker.Clear();
                lastContention = exception;
            }
            catch (Exception exception) when (IsPersistenceBoundaryFailure(exception))
            {
                context.ChangeTracker.Clear();
                throw Normalize("sending", workflowExecutionId, exception);
            }
        }

        throw Contention("sending", workflowExecutionId, lastContention);
    }

    public async ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> LeaseAsync(
        string workflowExecutionId,
        string ownerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        DistributedRuntimeIdentityConstraints.Validate(ownerId, nameof(ownerId));
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be positive.");
        DistributedRuntimeQueryLimits.ValidateTake(maxItems, nameof(maxItems));
        var leaseExpiresAt = checked(now + leaseDuration);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var scopeHash = EfDistributedIdentity.Hash(scope);
        var workflowHash = EfDistributedIdentity.Hash(workflowExecutionId);

        Exception? lastContention = null;
        for (var attempt = 0; attempt < MaxCasAttempts; attempt++)
        {
            context.ChangeTracker.Clear();
            try
            {
                await using var transaction = await BeginConsistencyTransactionAsync(cancellationToken);
                var head = await FindHeadAsync(scope, workflowExecutionId, cancellationToken);
                if (head is not null)
                {
                    EnsureHead(head, scope, workflowExecutionId);
                    await ValidateHeadStateAsync(head, scopeHash, workflowHash, scope, workflowExecutionId, cancellationToken);
                }

                var candidates = await context.CommandTransportItems.AsTracking()
                    .Where(row => row.ScopeKeyHash == scopeHash &&
                                  row.WorkflowExecutionIdHash == workflowHash &&
                                  row.ScopeKey == EfDistributedIdentity.EncodeScope(scope) &&
                                  row.WorkflowExecutionId == workflowExecutionId &&
                                  row.VisibleAtUtcTicks <= now.UtcTicks)
                    .OrderBy(row => row.Sequence)
                    .ThenBy(row => row.TransportItemIdHash)
                    .Take(maxItems)
                    .ToListAsync(cancellationToken);

                if (head is null)
                {
                    if (candidates.Count != 0 || await HasItemsAsync(scopeHash, workflowHash, scope, workflowExecutionId, cancellationToken))
                        throw new InvalidOperationException("Command transport items exist without their stream head.");
                    await transaction.CommitAsync(cancellationToken);
                    return [];
                }
                if (candidates.Count == 0)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return [];
                }

                foreach (var candidate in candidates)
                {
                    var current = MapItem(candidate, scope);
                    if (!StringComparer.Ordinal.Equals(current.WorkflowExecutionId, workflowExecutionId))
                        throw new InvalidOperationException("The command transport row belongs to a different workflow execution.");
                    if (!current.IsVisible(now))
                        continue;
                    if (current.Sequence > head.LastSequence)
                        throw new InvalidOperationException("A command transport item is ahead of its stream head.");

                    var next = current.Lease(ownerId, leaseExpiresAt);
                    ApplyLease(candidate, next, scope, scopeHash, workflowHash);
                }

                await context.SaveChangesAsync(cancellationToken);
                var summary = await ReadEarliestPendingAsync(scopeHash, workflowHash, scope, workflowExecutionId, head.PendingCount, head.LastSequence, cancellationToken);
                head.PendingVisibleAtUtcTicks = summary.VisibleAtUtcTicks;
                head.PendingSequence = summary.Sequence;
                head.Revision = checked(head.Revision + 1);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return candidates
                    .Select(row => MapItem(row, scope))
                    .Where(item => StringComparer.Ordinal.Equals(item.LeasedByOwnerId, ownerId) && item.LeaseExpiresAt == leaseExpiresAt)
                    .ToArray();
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
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
            {
                context.ChangeTracker.Clear();
                lastContention = exception;
            }
            catch (Exception exception) when (IsPersistenceBoundaryFailure(exception))
            {
                context.ChangeTracker.Clear();
                throw Normalize("leasing", workflowExecutionId, exception);
            }
        }

        throw Contention("leasing", workflowExecutionId, lastContention);
    }

    public async ValueTask<bool> AckAsync(
        string workflowExecutionId,
        string transportItemId,
        string ownerId,
        long leaseToken,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        ValidateTransportItemId(transportItemId);
        DistributedRuntimeIdentityConstraints.Validate(ownerId, nameof(ownerId));
        if (leaseToken <= 0)
            throw new ArgumentOutOfRangeException(nameof(leaseToken), "Lease token must be positive.");
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var scopeHash = EfDistributedIdentity.Hash(scope);
        var workflowHash = EfDistributedIdentity.Hash(workflowExecutionId);
        var transportItemIdHash = EfDistributedIdentity.Hash(transportItemId);
        var itemId = EfDistributedIdentity.CreateId(scope, transportItemId);
        Exception? lastContention = null;

        for (var attempt = 0; attempt < MaxCasAttempts; attempt++)
        {
            context.ChangeTracker.Clear();
            try
            {
                await using var transaction = await BeginConsistencyTransactionAsync(cancellationToken);
                var head = await FindHeadAsync(scope, workflowExecutionId, cancellationToken);
                var itemEntity = await context.CommandTransportItems.SingleOrDefaultAsync(
                    row => row.Id == itemId &&
                           row.ScopeKeyHash == scopeHash &&
                           row.WorkflowExecutionIdHash == workflowHash &&
                           row.TransportItemIdHash == transportItemIdHash &&
                           row.ScopeKey == EfDistributedIdentity.EncodeScope(scope) &&
                           row.WorkflowExecutionId == workflowExecutionId &&
                           row.TransportItemId == transportItemId,
                    cancellationToken);
                if (itemEntity is null)
                {
                    if (head is not null)
                    {
                        EnsureHead(head, scope, workflowExecutionId);
                        await ValidateHeadStateAsync(head, scopeHash, workflowHash, scope, workflowExecutionId, cancellationToken);
                    }
                    await transaction.CommitAsync(cancellationToken);
                    return false;
                }
                var item = MapItem(itemEntity, scope);
                if (!StringComparer.Ordinal.Equals(item.WorkflowExecutionId, workflowExecutionId) ||
                    !StringComparer.Ordinal.Equals(item.LeasedByOwnerId, ownerId) ||
                    item.LeaseToken != leaseToken ||
                    item.IsVisible(now))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return false;
                }

                if (head is null)
                    throw new InvalidOperationException("A command transport item exists without its stream head.");
                EnsureHead(head, scope, workflowExecutionId);
                await ValidateHeadStateAsync(head, scopeHash, workflowHash, scope, workflowExecutionId, cancellationToken);
                if (item.Sequence > head.LastSequence)
                    throw new InvalidOperationException("A command transport item is ahead of its stream head.");

                context.CommandTransportItems.Remove(itemEntity);
                head.PendingCount--;
                await context.SaveChangesAsync(cancellationToken);
                var summary = await ReadEarliestPendingAsync(scopeHash, workflowHash, scope, workflowExecutionId, head.PendingCount, head.LastSequence, cancellationToken);
                head.PendingVisibleAtUtcTicks = summary.VisibleAtUtcTicks;
                head.PendingSequence = summary.Sequence;
                head.Revision = checked(head.Revision + 1);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return true;
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
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
            {
                context.ChangeTracker.Clear();
                lastContention = exception;
            }
            catch (Exception exception) when (IsPersistenceBoundaryFailure(exception))
            {
                context.ChangeTracker.Clear();
                throw Normalize("acknowledging", workflowExecutionId, exception);
            }
        }

        throw Contention("acknowledging", workflowExecutionId, lastContention);
    }

    public async ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(
        DateTimeOffset now,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        DistributedRuntimeQueryLimits.ValidateTake(maxItems, nameof(maxItems));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var scopeHash = EfDistributedIdentity.Hash(scope);
        try
        {
            await using var transaction = await BeginConsistencyTransactionAsync(cancellationToken);
            var hasSuppressedVisibleItem = await context.CommandTransportItems.AsNoTracking()
                .AnyAsync(item =>
                    item.ScopeKeyHash == scopeHash &&
                    item.ScopeKey == EfDistributedIdentity.EncodeScope(scope) &&
                    item.VisibleAtUtcTicks <= now.UtcTicks &&
                    !context.CommandStreamHeads.Any(head =>
                        head.ScopeKeyHash == scopeHash &&
                        head.WorkflowExecutionIdHash == item.WorkflowExecutionIdHash &&
                        head.ScopeKey == EfDistributedIdentity.EncodeScope(scope) &&
                        head.WorkflowExecutionId == item.WorkflowExecutionId &&
                        head.PendingCount > 0 &&
                        head.PendingVisibleAtUtcTicks <= now.UtcTicks),
                    cancellationToken);
            if (hasSuppressedVisibleItem)
                throw new InvalidOperationException("A visible command transport row is missing a matching visible stream-head summary.");

            var rows = await context.CommandStreamHeads.AsNoTracking()
                .Where(row => row.ScopeKeyHash == scopeHash &&
                              row.ScopeKey == EfDistributedIdentity.EncodeScope(scope) &&
                              row.PendingCount > 0 &&
                              row.PendingVisibleAtUtcTicks <= now.UtcTicks)
                .OrderBy(row => row.WorkflowExecutionIdOrderKey)
                .ThenBy(row => row.Id)
                .Take(maxItems)
                .ToListAsync(cancellationToken);
            var ids = new List<string>(rows.Count);
            foreach (var row in rows)
            {
                EnsureHead(row, scope, row.WorkflowExecutionId);
                await ValidateHeadStateAsync(
                    row,
                    scopeHash,
                    EfDistributedIdentity.Hash(row.WorkflowExecutionId),
                    scope,
                    row.WorkflowExecutionId,
                    cancellationToken);
                ids.Add(row.WorkflowExecutionId);
            }
            await transaction.CommitAsync(cancellationToken);
            return ids.ToArray();
        }
        catch (OperationCanceledException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (IsPersistenceBoundaryFailure(exception))
        {
            context.ChangeTracker.Clear();
            throw Normalize("listing", $"scope:{scopeHash}", exception);
        }
    }

    public async ValueTask<int> CountPendingAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var scopeHash = EfDistributedIdentity.Hash(scope);
        var workflowHash = EfDistributedIdentity.Hash(workflowExecutionId);
        try
        {
            await using var transaction = await BeginConsistencyTransactionAsync(cancellationToken);
            var head = await FindHeadAsync(scope, workflowExecutionId, cancellationToken);
            if (head is null)
            {
                if (await HasItemsAsync(scopeHash, workflowHash, scope, workflowExecutionId, cancellationToken))
                    throw new InvalidOperationException("Command transport items exist without their stream head.");
                await transaction.CommitAsync(cancellationToken);
                return 0;
            }
            EnsureHead(head, scope, workflowExecutionId);
            await ValidateHeadStateAsync(head, scopeHash, workflowHash, scope, workflowExecutionId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return checked((int)head.PendingCount);
        }
        catch (OperationCanceledException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (IsPersistenceBoundaryFailure(exception))
        {
            context.ChangeTracker.Clear();
            throw Normalize("counting", workflowExecutionId, exception);
        }
    }

    private async ValueTask<ExecutionCommandStreamHeadEntity?> FindHeadAsync(
        string scope,
        string workflowExecutionId,
        CancellationToken cancellationToken) =>
        await context.CommandStreamHeads.SingleOrDefaultAsync(
            row => row.Id == EfDistributedIdentity.CreateId(scope, workflowExecutionId),
            cancellationToken);

    private async ValueTask<(long VisibleAtUtcTicks, long Sequence)> ReadEarliestPendingAsync(
        string scopeHash,
        string workflowHash,
        string scope,
        string workflowExecutionId,
        long expectedPendingCount,
        long lastSequence,
        CancellationToken cancellationToken)
    {
        var state = await ReadPendingStateAsync(
            scopeHash,
            workflowHash,
            scope,
            workflowExecutionId,
            lastSequence,
            cancellationToken);
        if (state.Count != expectedPendingCount)
            throw new InvalidOperationException("The command stream head pending count disagrees with transport rows.");
        return (state.VisibleAtUtcTicks, state.Sequence);
    }

    private async ValueTask ValidateHeadStateAsync(
        ExecutionCommandStreamHeadEntity head,
        string scopeHash,
        string workflowHash,
        string scope,
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        var state = await ReadPendingStateAsync(
            scopeHash,
            workflowHash,
            scope,
            workflowExecutionId,
            head.LastSequence,
            cancellationToken);
        if (state.Count != head.PendingCount ||
            state.VisibleAtUtcTicks != head.PendingVisibleAtUtcTicks ||
            state.Sequence != head.PendingSequence)
            throw new InvalidOperationException("The command stream head summary disagrees with transport rows.");
    }

    private async ValueTask<(long Count, long VisibleAtUtcTicks, long Sequence)> ReadPendingStateAsync(
        string scopeHash,
        string workflowHash,
        string scope,
        string workflowExecutionId,
        long lastSequence,
        CancellationToken cancellationToken)
    {
        var query = context.CommandTransportItems.AsNoTracking()
            .Where(candidate => candidate.ScopeKeyHash == scopeHash &&
                                candidate.WorkflowExecutionIdHash == workflowHash &&
                                candidate.ScopeKey == EfDistributedIdentity.EncodeScope(scope) &&
                                candidate.WorkflowExecutionId == workflowExecutionId);
        var count = await query.LongCountAsync(cancellationToken);
        if (count == 0)
            return (0, 0, 0);

        var maxSequence = await query.MaxAsync(candidate => candidate.Sequence, cancellationToken);
        if (maxSequence > lastSequence)
            throw new InvalidOperationException("A command transport item is ahead of its stream head.");

        var row = await query
            .OrderBy(candidate => candidate.VisibleAtUtcTicks)
            .ThenBy(candidate => candidate.Sequence)
            .ThenBy(candidate => candidate.TransportItemIdHash)
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
            throw new InvalidOperationException("The command transport row count disagrees with its earliest-row projection.");
        var item = MapItem(row, scope);
        if (!StringComparer.Ordinal.Equals(item.WorkflowExecutionId, workflowExecutionId))
            throw new InvalidOperationException("The earliest command transport row belongs to a different workflow execution.");
        return (count, row.VisibleAtUtcTicks, row.Sequence);
    }

    private async ValueTask<bool> HasItemsAsync(
        string scopeHash,
        string workflowHash,
        string scope,
        string workflowExecutionId,
        CancellationToken cancellationToken) =>
        await context.CommandTransportItems.AsNoTracking().AnyAsync(
            row => row.ScopeKeyHash == scopeHash &&
                   row.WorkflowExecutionIdHash == workflowHash &&
                   row.ScopeKey == EfDistributedIdentity.EncodeScope(scope) &&
                   row.WorkflowExecutionId == workflowExecutionId,
            cancellationToken);

    private static void ApplyLease(
        ExecutionCommandTransportItemEntity row,
        ExecutionCommandTransportItem item,
        string scope,
        string scopeHash,
        string workflowHash)
    {
        row.ScopeKey = EfDistributedIdentity.EncodeScope(scope);
        row.ScopeKeyHash = scopeHash;
        row.WorkflowExecutionIdHash = workflowHash;
        row.Sequence = item.Sequence;
        row.EnqueuedAtUtcTicks = item.EnqueuedAt.UtcTicks;
        row.EnqueuedAtOffsetMinutes = OffsetMinutes(item.EnqueuedAt);
        row.VisibleAtUtcTicks = item.LeaseExpiresAt?.UtcTicks ?? 0;
        row.LeaseOwnerId = item.LeasedByOwnerId;
        row.LeaseToken = item.LeaseToken ?? 0;
        row.LeaseExpiresAtUtcTicks = item.LeaseExpiresAt?.UtcTicks ?? 0;
        row.LeaseExpiresAtOffsetMinutes = item.LeaseExpiresAt is { } expiry ? OffsetMinutes(expiry) : 0;
        row.PayloadJson = Serialize(item);
        row.Revision = checked(row.Revision + 1);
    }

    private static ExecutionCommandTransportItemEntity ToEntity(
        ExecutionCommandTransportItem item,
        string scope,
        string scopeHash,
        string workflowHash) => new()
        {
            Id = EfDistributedIdentity.CreateId(scope, item.TransportItemId),
            ScopeKey = EfDistributedIdentity.EncodeScope(scope),
            ScopeKeyHash = scopeHash,
            TransportItemId = item.TransportItemId,
            TransportItemIdHash = EfDistributedIdentity.Hash(item.TransportItemId),
            WorkflowExecutionId = item.WorkflowExecutionId,
            WorkflowExecutionIdHash = workflowHash,
            Sequence = item.Sequence,
            EnqueuedAtUtcTicks = item.EnqueuedAt.UtcTicks,
            EnqueuedAtOffsetMinutes = OffsetMinutes(item.EnqueuedAt),
            VisibleAtUtcTicks = 0,
            LeaseOwnerId = null,
            LeaseToken = 0,
            LeaseExpiresAtUtcTicks = 0,
            LeaseExpiresAtOffsetMinutes = 0,
            PayloadJson = Serialize(item),
            Revision = 1
        };

    private static ExecutionCommandTransportItem MapItem(
        ExecutionCommandTransportItemEntity row,
        string scope)
    {
        EnsureItemIdentity(row, scope);
        var item = Deserialize<ExecutionCommandTransportItem>(row.PayloadJson);
        var rowEnqueuedAt = ReadTimestamp(row.EnqueuedAtUtcTicks, row.EnqueuedAtOffsetMinutes);
        var rowLeaseExpiresAt = row.LeaseOwnerId is null
            ? (DateTimeOffset?)null
            : ReadTimestamp(row.LeaseExpiresAtUtcTicks, row.LeaseExpiresAtOffsetMinutes);
        if (!StringComparer.Ordinal.Equals(item.TransportItemId, row.TransportItemId) ||
            !StringComparer.Ordinal.Equals(item.WorkflowExecutionId, row.WorkflowExecutionId) ||
            item.Sequence != row.Sequence ||
            item.EnqueuedAt != rowEnqueuedAt ||
            item.EnqueuedAt.Offset != rowEnqueuedAt.Offset ||
            item.DeliveryAttemptCount != checked((int)row.LeaseToken) ||
            !StringComparer.Ordinal.Equals(item.LeasedByOwnerId, row.LeaseOwnerId) ||
            item.LeaseExpiresAt != rowLeaseExpiresAt ||
            (item.LeaseExpiresAt is { } itemExpiry && rowLeaseExpiresAt is { } rowExpiry && itemExpiry.Offset != rowExpiry.Offset) ||
            row.VisibleAtUtcTicks != (item.LeaseExpiresAt?.UtcTicks ?? 0))
            throw new InvalidOperationException("The command transport row contains inconsistent typed projections.");
        EnsurePartition(item.Envelope, scope);
        return item;
    }

    private static void EnsureItemIdentity(ExecutionCommandTransportItemEntity row, string scope)
    {
        if (!StringComparer.Ordinal.Equals(row.ScopeKey, EfDistributedIdentity.EncodeScope(scope)) ||
            !StringComparer.Ordinal.Equals(row.ScopeKeyHash, EfDistributedIdentity.Hash(scope)) ||
            !StringComparer.Ordinal.Equals(row.WorkflowExecutionIdHash, EfDistributedIdentity.Hash(row.WorkflowExecutionId)) ||
            !StringComparer.Ordinal.Equals(row.TransportItemIdHash, EfDistributedIdentity.Hash(row.TransportItemId)) ||
            !StringComparer.Ordinal.Equals(row.Id, EfDistributedIdentity.CreateId(scope, row.TransportItemId)) ||
            !StringComparer.Ordinal.Equals(row.TransportItemId, ComposeTransportItemId(row.WorkflowExecutionId, row.Sequence)) ||
            row.Sequence <= 0 || row.Revision <= 0 ||
            (row.LeaseOwnerId is null && (row.LeaseToken != 0 || row.LeaseExpiresAtUtcTicks != 0 || row.LeaseExpiresAtOffsetMinutes != 0)) ||
            (row.LeaseOwnerId is not null && (row.LeaseToken <= 0 || row.LeaseExpiresAtUtcTicks <= 0)))
            throw new InvalidOperationException("The command transport row contains inconsistent identity projections.");
    }

    private static void EnsureHead(ExecutionCommandStreamHeadEntity row, string scope, string expectedWorkflowExecutionId)
    {
        if (!StringComparer.Ordinal.Equals(row.ScopeKey, EfDistributedIdentity.EncodeScope(scope)) ||
            !StringComparer.Ordinal.Equals(row.ScopeKeyHash, EfDistributedIdentity.Hash(scope)) ||
            !StringComparer.Ordinal.Equals(row.WorkflowExecutionId, expectedWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(row.WorkflowExecutionIdHash, EfDistributedIdentity.Hash(row.WorkflowExecutionId)) ||
            !StringComparer.Ordinal.Equals(row.Id, EfDistributedIdentity.CreateId(scope, row.WorkflowExecutionId)) ||
            !row.WorkflowExecutionIdOrderKey.AsSpan().SequenceEqual(EfDistributedIdentity.CreateOrderKey(row.WorkflowExecutionId, ExecutionCommandTransportEfModule.WorkflowExecutionIdOrderKeyWidth)) ||
            row.LastSequence <= 0 || row.PendingCount < 0 || row.PendingVisibleAtUtcTicks < 0 || row.PendingSequence < 0 || row.Revision <= 0 ||
            (row.PendingCount == 0 && (row.PendingSequence != 0 || row.PendingVisibleAtUtcTicks != 0)) ||
            (row.PendingCount > 0 && (row.PendingSequence <= 0 || row.PendingSequence > row.LastSequence)))
            throw new InvalidOperationException("The command stream head contains inconsistent identity or summary projections.");
    }

    private Task<IDbContextTransaction> BeginConsistencyTransactionAsync(CancellationToken cancellationToken) =>
        context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

    private string RequireScope() => accessContextAccessor.Current.Scope?.Value ??
        throw new InvalidOperationException("EF distributed stores require a scoped persistence access context.");

    private static void EnsurePartition(WorkflowExecutionCommandEnvelope envelope, string scope) =>
        EnsurePartitionCore(envelope, scope);

    private static void EnsurePartitionCore(WorkflowExecutionCommandEnvelope envelope, string scope)
    {
        if (!StringComparer.Ordinal.Equals(envelope.Partition.Value, scope) ||
            !StringComparer.Ordinal.Equals(envelope.WorkflowExecutionId, envelope.Command.WorkflowExecutionId))
            throw new InvalidOperationException("The command envelope contains an inconsistent partition or execution identity.");
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidOperationException("The command transport payload is null.");

    private static DateTimeOffset ReadTimestamp(long utcTicks, int offsetMinutes)
    {
        if (offsetMinutes is < -14 * 60 or > 14 * 60)
            throw new InvalidOperationException("The command transport row contains an invalid timestamp offset.");
        try
        {
            return new DateTimeOffset(utcTicks, TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(offsetMinutes));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("The command transport row contains an invalid timestamp.", exception);
        }
    }

    private static int OffsetMinutes(DateTimeOffset value) => checked((int)value.Offset.TotalMinutes);

    private static void ValidateTransportItemId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > ExecutionCommandTransportEfModule.TransportItemIdMaximumLength)
            throw new ArgumentException($"A transport item ID cannot exceed {ExecutionCommandTransportEfModule.TransportItemIdMaximumLength} UTF-16 code units.", nameof(value));
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsLowSurrogate(value[index]) || (char.IsHighSurrogate(value[index]) && (++index >= value.Length || !char.IsLowSurrogate(value[index]))))
                throw new ArgumentException("A transport item ID must contain well-formed Unicode.", nameof(value));
        }
    }

    private static string ComposeTransportItemId(string workflowExecutionId, long sequence) =>
        $"transport:{Escape(workflowExecutionId)}:{sequence}";

    private static string Escape(string value) => value.Replace("%", "%25").Replace(":", "%3A");

    private static bool IsPersistenceBoundaryFailure(Exception exception) =>
        exception is DbException or DbUpdateException or InvalidOperationException or ArgumentException or OverflowException or JsonException;

    private static ExecutionCommandTransportEntityFrameworkPersistenceException Normalize(string operation, string identity, Exception inner) =>
        new(operation, identity, $"The EF execution command transport failed while {operation} command '{identity}'.", inner);

    private static ExecutionCommandTransportEntityFrameworkPersistenceException Contention(string operation, string identity, Exception? inner) =>
        new(operation, identity, $"The EF execution command transport could not complete {operation} after {MaxCasAttempts} bounded compare-and-swap attempts.", inner ?? new InvalidOperationException("No contention exception was supplied."));

}
