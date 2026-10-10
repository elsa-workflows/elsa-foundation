using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed class EfWorkflowExecutableStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IWorkflowExecutableStore
{
    private static readonly EfWriteRetry Coordination = new(EfWriteRetry.DefaultMaxAttempts, EfWriteConflict.Concurrency);
    // A lease row conflicts only with an acquirer of the same lease id: a concurrent first insert or expired-row overwrite.
    private static readonly EfWriteRetry LeaseWrite = new(EfWriteRetry.DefaultMaxAttempts, EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey);

    public ValueTask SaveAsync(
        WorkflowExecutable executable,
        CancellationToken cancellationToken = default) =>
        SaveBatchAsync([executable], cancellationToken);

    public async ValueTask SaveBatchAsync(
        IReadOnlyList<WorkflowExecutable> executables,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executables);
        cancellationToken.ThrowIfCancellationRequested();
        if (executables.Select(x => x.Identity.ArtifactId).Distinct(StringComparer.Ordinal).Count() != executables.Count)
            throw new ArgumentException("A workflow executable batch must contain distinct artifact ids.", nameof(executables));
        foreach (var item in executables)
            Validate(item);
        if (executables.Count == 0)
            return;

        var scope = RequireScope();
        context.ChangeTracker.Clear();
        var pending = new List<(WorkflowExecutable Executable, string Id)>();
        try
        {
            foreach (var item in executables)
            {
                var artifactId = item.Identity.ArtifactId;
                var id = CreateId(scope, artifactId);
                var (artifact, coordination) = await FindPairAsync(scope, artifactId, id, cancellationToken);
                if (artifact is null && coordination is null)
                    pending.Add((item, id));
                else if (artifact is null || coordination is null)
                {
                    throw new InvalidDataException($"Workflow executable '{id}' has incomplete persisted state.");
                }
                else
                {
                    var current = Read(artifact, scope, artifactId, id);
                    _ = ReadCoordination(coordination, scope, artifactId, id);
                    EnsureMatchingIncarnation(artifact, coordination, id);
                    EnsureSameIdentityAndContent(current, item);
                }
            }

            if (pending.Count == 0)
            {
                context.ChangeTracker.Clear();
                return;
            }

            await using var transaction = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
                context, "saving", scope, () => context.Database.BeginTransactionAsync(cancellationToken));
            foreach (var (item, id) in pending)
            {
                var incarnationId = NewIncarnationId();
                context.WorkflowExecutables.Add(ToEntity(
                    item,
                    scope,
                    id,
                    RuntimeArtifactJson.Serialize(item),
                    incarnationId));
                context.WorkflowExecutableCoordinations.Add(ToCoordinationEntity(item.Identity.ArtifactId, scope, id, incarnationId));
            }
            await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                context, "saving", scope, () => context.SaveChangesAsync(cancellationToken));
            await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                context, "saving", scope, () => transaction.CommitAsync(cancellationToken));
        }
        catch (Exception exception) when (
            EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey | EfWriteConflict.Transient))
        {
            context.ChangeTracker.Clear();
            try
            {
                await ReconcileCreateAsync(executables, scope, exception, cancellationToken);
            }
            finally
            {
                context.ChangeTracker.Clear();
            }
        }
        // Already normalized by an inner call: rethrow rather than let the clause below wrap it a second time,
        // which it would, because this store's exception derives from InvalidOperationException and carries the
        // provider failure as its inner.
        catch (RuntimeArtifactEntityFrameworkPersistenceException) { throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            context.ChangeTracker.Clear();
            throw NormalizeProviderFailure("saving", string.Join(",", executables.Select(x => x.Identity.ArtifactId)), exception);
        }
        catch
        {
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private async ValueTask ReconcileCreateAsync(
        IReadOnlyList<WorkflowExecutable> executables,
        string scope,
        Exception cause,
        CancellationToken cancellationToken)
    {
        foreach (var executable in executables)
        {
            var artifactId = executable.Identity.ArtifactId;
            var pair = await LoadPairAsync(scope, artifactId, cancellationToken);
            if (pair is null)
                throw NormalizeProviderFailure("saving", artifactId, cause);

            var current = Read(pair.Value.Artifact, scope, artifactId, CreateId(scope, artifactId));
            EnsureSameIdentityAndContent(current, executable);
        }

        context.ChangeTracker.Clear();
    }

    private static void EnsureSameIdentityAndContent(WorkflowExecutable current, WorkflowExecutable candidate)
    {
        if (!StringComparer.Ordinal.Equals(current.Identity.ArtifactHash, candidate.Identity.ArtifactHash))
            throw new InvalidOperationException($"Workflow executable '{candidate.Identity.ArtifactId}' is already bound to different artifact content.");
    }

    private static void EnsureMatchingIncarnation(
        WorkflowExecutableEntity artifact,
        WorkflowExecutableCoordinationEntity coordination,
        string id)
    {
        if (!StringComparer.Ordinal.Equals(artifact.IncarnationId, coordination.IncarnationId))
            throw new InvalidDataException($"Workflow executable '{id}' has mismatched incarnation identities.");
    }
    public async ValueTask<WorkflowExecutable?> FindAsync(
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var id = CreateId(scope, artifactId);
        var row = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
            context,
            "finding",
            artifactId,
            () => context.WorkflowExecutables
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Id == id &&
                         x.ScopeKeyHash == Hash(scope) &&
                         x.ScopeKey == Encode(scope) &&
                         x.ArtifactIdHash == Hash(artifactId) &&
                         x.ArtifactId == Encode(artifactId),
                    cancellationToken));
        return row is null ? null : Read(row, scope, artifactId, id);
    }

    public async ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(
        RuntimeStorePageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = RequireScope();
        var cursor = Decode(request.ContinuationToken, scope);
        var scopeHash = Hash(scope);
        var encodedScope = Encode(scope);
        var rows = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
            context,
            "listing",
            scope,
            () => context.WorkflowExecutables
                .AsNoTracking()
                .Where(x => x.ScopeKeyHash == scopeHash && x.ScopeKey == encodedScope &&
                            (cursor == null || x.ArtifactIdOrderKey.CompareTo(cursor) > 0))
                .OrderBy(x => x.ArtifactIdOrderKey)
                .Take(request.Limit + 1)
                .ToArrayAsync(cancellationToken));
        var more = rows.Length > request.Limit;
        if (more)
            rows = rows[..request.Limit];
        var items = rows.Select(x => Read(x, scope, Decode(x.ArtifactId), x.Id)).ToArray();
        var continuation = more ? Encode(rows[^1].ArtifactIdOrderKey, scope) : null;
        return new RuntimeStorePage<WorkflowExecutable>(request, items, continuation);
    }
    public async ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var pair = await LoadPairAsync(scope, artifactId, cancellationToken);
        return pair is not null && await DeletePairAsync(
            scope,
            artifactId,
            null,
            default,
            pair.Value.Artifact.IncarnationId,
            cancellationToken);
    }
    // Root-write leases (spec 200). Each lease is a row of its own, so a holder's acquire, renewal and release touch only
    // that row and never contend with other holders of the artifact (FR-001, FR-002). The deletion guard stays on the
    // coordination row. The two sides exclude each other by write-then-check (research R2): each commits its own record
    // and then reads the other side's in a fresh statement, so they cannot both succeed. Every lease operation runs on a
    // context of its own (CreateLeaseContext), so it never saves or discards what a caller staged on the shared one.
    public async ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(
        string artifactId,
        string leaseId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ValidateTransition(artifactId, leaseId, expiresAt, now);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var id = CreateLeaseId(scope, artifactId, leaseId);
        await using var leaseContext = CreateLeaseContext();
        try
        {
            var grant = await LeaseWrite.RunAsync<LeaseGrant?>(leaseContext, async () =>
            {
                leaseContext.ChangeTracker.Clear();
                var pair = await ReadPairAsync(leaseContext, scope, artifactId, cancellationToken);
                if (pair is not { } current || GuardIsLive(current.State, now))
                    return null;
                // A guard still on the row may belong to a delete whose transaction outlived it: clear it first, by the
                // row's revision, so that delete fails its own revision check instead of deleting under this lease. A lost
                // race throws a concurrency conflict and the attempt starts over.
                if (current.State.Guard is not null &&
                    !await ClearExpiredGuardAsync(leaseContext, scope, artifactId, current.IncarnationId, now, cancellationToken))
                    return null;
                var row = await FindLeaseAsync(leaseContext, scope, artifactId, leaseId, id, cancellationToken);
                if (row is not null && row.IncarnationId == current.IncarnationId && row.ExpiresAtUtcTicks > now.UtcTicks)
                    return new LeaseGrant(row.Token, current.IncarnationId, Created: false);

                var token = Token();
                if (row is null)
                {
                    leaseContext.WorkflowExecutableRootWriteLeases.Add(ToLeaseEntity(scope, artifactId, leaseId, id, token, expiresAt, current.IncarnationId));
                }
                else
                {
                    // An expired row, or one left by an earlier incarnation, is overwritten under its revision.
                    row.Token = token;
                    row.ExpiresAtUtcTicks = expiresAt.UtcTicks;
                    row.IncarnationId = current.IncarnationId;
                    row.SchemaVersion = RuntimeArtifactEfModule.SchemaVersion;
                    row.Revision++;
                }

                await leaseContext.SaveChangesAsync(cancellationToken);
                return new LeaseGrant(token, current.IncarnationId, Created: true);
            }, _ => throw CoordinationDidNotSettle(artifactId), cancellationToken);

            if (grant is null)
                return null;

            // Write-then-check: the lease is committed, so a guard that commits from here on sees it and stands down. A
            // guard, delete or recreate that this fresh read observes means this lease came too late: withdraw it.
            if (!await LeaseWrite.RunAsync(
                    leaseContext,
                    () => new ValueTask<bool>(StillAdmitsAsync(leaseContext, scope, artifactId, grant.IncarnationId, now, cancellationToken)),
                    _ => ValueTask.FromResult(false),
                    cancellationToken))
            {
                if (grant.Created)
                    await DeleteLeaseAsync(leaseContext, id, grant.Token, cancellationToken);
                return null;
            }

            return new WorkflowExecutableRootWriteLease(artifactId, leaseId, grant.Token);
        }
        // Already normalized by an inner call: rethrow rather than let the clause below wrap it a second time,
        // which it would, because this store's exception derives from InvalidOperationException and carries the
        // provider failure as its inner.
        catch (RuntimeArtifactEntityFrameworkPersistenceException) { throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            throw NormalizeProviderFailure("leasing", artifactId, exception);
        }
    }

    public async ValueTask<bool> RenewRootWriteLeaseAsync(
        WorkflowExecutableRootWriteLease lease,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ValidateTransition(lease.ArtifactId, lease.LeaseId, expiresAt, now);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var id = CreateLeaseId(scope, lease.ArtifactId, lease.LeaseId);
        var token = lease.ConcurrencyToken;
        var nowTicks = now.UtcTicks;
        var expiresAtTicks = expiresAt.UtcTicks;
        await using var leaseContext = CreateLeaseContext();
        try
        {
            // One statement on this lease's own row. The revision moves too, so a holder whose clock already counts the
            // row expired cannot overwrite it on the strength of what it read before this renewal.
            var renewed = await leaseContext.WorkflowExecutableRootWriteLeases
                .Where(x => x.Id == id && x.Token == token && x.ExpiresAtUtcTicks > nowTicks)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(x => x.ExpiresAtUtcTicks, expiresAtTicks)
                    .SetProperty(x => x.Revision, x => x.Revision + 1), cancellationToken);
            return renewed == 1;
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            throw NormalizeProviderFailure("renewing", lease.ArtifactId, exception);
        }
    }

    public async ValueTask ReleaseRootWriteLeaseAsync(
        WorkflowExecutableRootWriteLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(lease.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(lease.LeaseId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var id = CreateLeaseId(scope, lease.ArtifactId, lease.LeaseId);
        await using var leaseContext = CreateLeaseContext();
        try
        {
            await DeleteLeaseAsync(leaseContext, id, lease.ConcurrencyToken, cancellationToken);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            throw NormalizeProviderFailure("releasing", lease.ArtifactId, exception);
        }
    }

    public async ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(
        string artifactId,
        string operationId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ValidateTransition(artifactId, operationId, expiresAt, now);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var begun = await Coordination.RunAsync<BegunDeletion?>(context, async () =>
        {
            context.ChangeTracker.Clear();
            var pair = await LoadPairAsync(scope, artifactId, cancellationToken);
            if (pair is null)
                return null;
            var row = pair.Value.Coordination;
            var state = ReadCoordination(row, scope, artifactId, CreateId(scope, artifactId));
            // Leases still in the shared record from before spec 200 hold the artifact until they expire (owner decision 1).
            var leases = LiveLeases(state, now);
            if (leases.Count != 0)
                return null;
            if (state.Guard is { } existing && existing.ExpiresAt > now)
                return existing.OperationId == operationId
                    ? new BegunDeletion(new WorkflowExecutableDeletionGuard(artifactId, operationId, existing.Token), row.IncarnationId, Created: false)
                    : null;
            var created = new Guard(operationId, Token(), expiresAt);
            await UpdateCoordination(row, new CoordinationState(leases, created), cancellationToken);
            context.ChangeTracker.Clear();
            return new BegunDeletion(new WorkflowExecutableDeletionGuard(artifactId, operationId, created.Token), row.IncarnationId, Created: true);
        }, _ => throw new InvalidOperationException("Runtime coordination changed concurrently."), cancellationToken);

        if (begun is null)
            return null;
        if (!begun.Created)
            return begun.Guard;

        // Write-then-check: the guard is committed, so an acquirer that commits from here on sees it and withdraws. A live
        // lease this fresh read observes was committed first: stand down.
        try
        {
            if (await HasLiveLeasesAsync(context, scope, artifactId, begun.IncarnationId, now, cancellationToken))
            {
                await CancelDeletionAsync(begun.Guard, cancellationToken);
                return null;
            }

            await PurgeDeadLeasesAsync(scope, artifactId, begun.IncarnationId, now, cancellationToken);
            return begun.Guard;
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            throw NormalizeProviderFailure("guarding", artifactId, exception);
        }
    }

    public async ValueTask<bool> CancelDeletionAsync(
        WorkflowExecutableDeletionGuard guard,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.OperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.ConcurrencyToken);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        return await Coordination.RunAsync(context, async () =>
        {
            var pair = await LoadPairAsync(scope, guard.ArtifactId, cancellationToken);
            if (pair is null)
                return false;
            var row = pair.Value.Coordination;
            var state = ReadCoordination(row, scope, guard.ArtifactId, CreateId(scope, guard.ArtifactId));
            if (state.Guard is not { } current || current.OperationId != guard.OperationId || current.Token != guard.ConcurrencyToken)
                return false;
            await UpdateCoordination(row, new CoordinationState(state.Leases, null), cancellationToken);
            context.ChangeTracker.Clear();
            return true;
        }, _ => throw CoordinationDidNotSettle(guard.ArtifactId), cancellationToken);
    }
    public async ValueTask<bool> DeleteAsync(
        WorkflowExecutableDeletionGuard guard,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.OperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.ConcurrencyToken);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var pair = await LoadPairAsync(scope, guard.ArtifactId, cancellationToken);
        return pair is not null && await DeletePairAsync(
            scope,
            guard.ArtifactId,
            guard,
            now,
            pair.Value.Artifact.IncarnationId,
            cancellationToken);
    }

    private async Task<bool> DeletePairAsync(
        string scope,
        string artifactId,
        WorkflowExecutableDeletionGuard? guard,
        DateTimeOffset now,
        string expectedIncarnationId,
        CancellationToken cancellationToken)
    {
        var id = CreateId(scope, artifactId);

        return await Coordination.RunAsync(context, async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
                context, "deleting", artifactId, () => context.Database.BeginTransactionAsync(cancellationToken));

            try
            {
                var (artifact, coordination) = await FindPairAsync(scope, artifactId, id, cancellationToken);

                if (artifact is null && coordination is null)
                {
                    await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                        context, "deleting", artifactId, () => transaction.CommitAsync(cancellationToken));
                    return false;
                }

                if (artifact is null || coordination is null)
                    throw new InvalidDataException($"Workflow executable '{id}' has incomplete persisted state.");

                if (artifact.IncarnationId != expectedIncarnationId || coordination.IncarnationId != expectedIncarnationId)
                {
                    await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                        context, "deleting", artifactId, () => transaction.CommitAsync(cancellationToken));
                    return false;
                }

                _ = Read(artifact, scope, artifactId, id);
                var state = ReadCoordination(coordination, scope, artifactId, id);

                if (guard is not null &&
                    (!CanDelete(state, guard, now) ||
                     await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
                         context, "deleting", artifactId,
                         () => HasLiveLeasesAsync(context, scope, artifactId, expectedIncarnationId, now, cancellationToken))))
                {
                    await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                        context, "deleting", artifactId, () => transaction.CommitAsync(cancellationToken));
                    return false;
                }

                var scopeHash = Hash(scope);
                var encodedScope = Encode(scope);
                var artifactIdHash = Hash(artifactId);
                var encodedArtifactId = Encode(artifactId);
                await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                    context, "deleting", artifactId, () => context.WorkflowExecutableRootWriteLeases
                        .Where(x => x.ScopeKeyHash == scopeHash && x.ScopeKey == encodedScope &&
                                    x.ArtifactIdHash == artifactIdHash && x.ArtifactId == encodedArtifactId)
                        .ExecuteDeleteAsync(cancellationToken));
                context.RemoveRange(artifact, coordination);
                await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                    context, "deleting", artifactId, () => context.SaveChangesAsync(cancellationToken));
                await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                    context, "deleting", artifactId, () => transaction.CommitAsync(cancellationToken));
                return true;
            }
            catch (Exception exception)
            {
                await RollbackQuietlyAsync(transaction);
                context.ChangeTracker.Clear();
                if (exception is DbUpdateException and not DbUpdateConcurrencyException or DbException)
                    throw NormalizeProviderFailure("deleting", artifactId, exception);
                throw;
            }
        }, lastContention => throw new InvalidOperationException("The workflow executable changed concurrently; retry the operation.", lastContention), cancellationToken);
    }

    private static async Task RollbackQuietlyAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception exception) when (IsCatchable(exception))
        {
            // Best-effort cleanup must not mask the operation's original exception.
        }
    }

    private static bool IsCatchable(Exception exception) =>
        exception is not (OutOfMemoryException or
            StackOverflowException or
            AccessViolationException or
            AppDomainUnloadedException or
            BadImageFormatException or
            CannotUnloadAppDomainException or
            InvalidProgramException);

    private async Task<WorkflowExecutableEntity?> FindExecutableAsync(
        string scope,
        string artifactId,
        string id,
        CancellationToken cancellationToken) =>
        await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
            context,
            "reading",
            artifactId,
            () => context.WorkflowExecutables.SingleOrDefaultAsync(
                x => x.Id == id &&
                     x.ScopeKeyHash == Hash(scope) &&
                     x.ScopeKey == Encode(scope) &&
                     x.ArtifactIdHash == Hash(artifactId) &&
                     x.ArtifactId == Encode(artifactId),
                cancellationToken));

    private async Task<WorkflowExecutableCoordinationEntity?> FindCoordinationAsync(
        string scope,
        string artifactId,
        string id,
        CancellationToken cancellationToken) =>
        await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
            context,
            "reading",
            artifactId,
            () => context.WorkflowExecutableCoordinations.SingleOrDefaultAsync(
                x => x.Id == id &&
                     x.ScopeKeyHash == Hash(scope) &&
                     x.ScopeKey == Encode(scope) &&
                     x.ArtifactIdHash == Hash(artifactId) &&
                     x.ArtifactId == Encode(artifactId),
                cancellationToken));
    // A writer can commit between these statements. Reobserve an incoherent pair once, clearing tracked
    // entities so a delete/recreate cannot keep the old incarnation. Callers still classify the final pair.
    private async Task<(WorkflowExecutableEntity? Artifact, WorkflowExecutableCoordinationEntity? Coordination)> FindPairAsync(
        string scope,
        string artifactId,
        string id,
        CancellationToken cancellationToken)
    {
        (WorkflowExecutableEntity? Artifact, WorkflowExecutableCoordinationEntity? Coordination) pair = default;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
                context.ChangeTracker.Clear();
            var artifact = await FindExecutableAsync(scope, artifactId, id, cancellationToken);
            var coordination = await FindCoordinationAsync(scope, artifactId, id, cancellationToken);
            pair = (artifact, coordination);
            if (artifact is null && coordination is null ||
                artifact is not null && coordination is not null &&
                StringComparer.Ordinal.Equals(artifact.IncarnationId, coordination.IncarnationId))
                break;
        }
        return pair;
    }

    private async Task<(WorkflowExecutableEntity Artifact, WorkflowExecutableCoordinationEntity Coordination)?> LoadPairAsync(
        string scope,
        string artifactId,
        CancellationToken cancellationToken)
    {
        var id = CreateId(scope, artifactId);
        var (artifact, coordination) = await FindPairAsync(scope, artifactId, id, cancellationToken);
        if (artifact is null && coordination is null)
            return null;
        if (artifact is null || coordination is null)
            throw new InvalidDataException($"Workflow executable '{id}' has incomplete persisted state.");
        EnsureMatchingIncarnation(artifact, coordination, id);

        _ = Read(artifact, scope, artifactId, id);
        _ = ReadCoordination(coordination, scope, artifactId, id);
        return (artifact, coordination);
    }

    /// <summary>Saves the coordination row; a lost revision race clears tracking and propagates for the caller's retry.</summary>
    private async Task UpdateCoordination(WorkflowExecutableCoordinationEntity row, CoordinationState state, CancellationToken ct)
    {
        row.ContentJson = RuntimeArtifactJson.Serialize(state);
        row.SchemaVersion = RuntimeArtifactEfModule.SchemaVersion;
        row.Revision++;
        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        // Already normalized by an inner call: rethrow rather than let the clause below wrap it a second time,
        // which it would, because this store's exception derives from InvalidOperationException and carries the
        // provider failure as its inner.
        catch (RuntimeArtifactEntityFrameworkPersistenceException) { throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsStoreBoundaryFailure(exception))
        {
            context.ChangeTracker.Clear();
            throw NormalizeProviderFailure("updating coordination", row.ArtifactId, exception);
        }
        catch
        {
            context.ChangeTracker.Clear();
            throw;
        }
    }

    // A sibling of the injected context, built from its options: the same provider, interceptors and schema write gate,
    // but its own change tracker and, in a host (which configures a connection string), its own pooled connection. A
    // lease write therefore never saves or discards what a caller staged on the shared context, and a renewal that fires
    // while a checkpoint write is using the shared context does not touch it (spec 200, research R4).
    private RuntimeDbContext CreateLeaseContext()
    {
        var options = context.GetService<IDbContextOptions>();
        try
        {
            return (RuntimeDbContext)Activator.CreateInstance(context.GetType(), options)!;
        }
        catch (MissingMethodException exception)
        {
            throw new InvalidOperationException(
                $"Runtime root-write leases run on a context of their own, so '{context.GetType().Name}' needs a public constructor that takes only its options.",
                exception);
        }
    }

    /// <summary>
    /// Reads and validates the executable pair without tracking: its incarnation and coordination state, or null when absent.
    /// </summary>
    private static async Task<(string IncarnationId, CoordinationState State)?> ReadPairAsync(
        RuntimeDbContext db,
        string scope,
        string artifactId,
        CancellationToken cancellationToken)
    {
        var id = CreateId(scope, artifactId);
        var scopeHash = Hash(scope);
        var encodedScope = Encode(scope);
        var artifactIdHash = Hash(artifactId);
        var encodedArtifactId = Encode(artifactId);
        for (var attempt = 0; ; attempt++)
        {
            var executable = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
                db, "reading", artifactId, () => db.WorkflowExecutables
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.Id == id && x.ScopeKeyHash == scopeHash && x.ScopeKey == encodedScope &&
                             x.ArtifactIdHash == artifactIdHash && x.ArtifactId == encodedArtifactId,
                        cancellationToken));
            if (executable is not null)
                _ = Read(executable, scope, artifactId, id);
            var artifact = executable?.IncarnationId;
            var coordination = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
                db, "reading", artifactId, () => db.WorkflowExecutableCoordinations
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.Id == id && x.ScopeKeyHash == scopeHash && x.ScopeKey == encodedScope &&
                             x.ArtifactIdHash == artifactIdHash && x.ArtifactId == encodedArtifactId,
                        cancellationToken));
            if (artifact is null && coordination is null)
                return null;
            var state = coordination is null ? null : ReadCoordination(coordination, scope, artifactId, id);
            if (artifact is not null && state is not null && StringComparer.Ordinal.Equals(artifact, coordination!.IncarnationId))
                return (artifact, state);
            // A delete or create committed between the two reads; observe the pair once more before calling it corrupt.
            if (attempt > 0)
                throw new InvalidDataException($"Workflow executable '{id}' has incomplete persisted state.");
        }
    }

    // The check after a lease commit. The executable and its coordination row are created and deleted in one transaction,
    // so the coordination row alone answers it: present, of the lease's incarnation, and without a live guard. An expired
    // guard still on the row is cleared by revision first (see ClearExpiredGuardAsync); losing that race throws a
    // concurrency conflict, and the caller checks again.
    private static async Task<bool> StillAdmitsAsync(
        RuntimeDbContext db,
        string scope,
        string artifactId,
        string incarnationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var coordination = await FindCoordinationAsync(db, scope, artifactId, tracking: false, cancellationToken);
        if (coordination is null)
            return false;
        var state = ReadCoordination(coordination, scope, artifactId, CreateId(scope, artifactId));
        if (!StringComparer.Ordinal.Equals(coordination.IncarnationId, incarnationId))
            return false;
        return state.Guard is null || await ClearExpiredGuardAsync(db, scope, artifactId, incarnationId, now, cancellationToken);
    }

    /// <summary>
    /// Removes an expired deletion guard from the coordination row by compare-and-swap on its revision, and returns false
    /// when the guard is live, or the pair is gone or recreated. A guarded delete deletes the coordination row under the
    /// revision it read, so whichever of the two commits first makes the other fail: a delete whose transaction outlived its
    /// guard can no longer commit under a lease granted meanwhile (the per-lease rows alone would not stop it). Only
    /// acquirers that meet a guard write the row, so it does not become a hot row again.
    /// </summary>
    /// <exception cref="DbUpdateConcurrencyException">The row changed since it was read.</exception>
    private static async Task<bool> ClearExpiredGuardAsync(
        RuntimeDbContext db,
        string scope,
        string artifactId,
        string incarnationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        try
        {
            var row = await FindCoordinationAsync(db, scope, artifactId, tracking: true, cancellationToken);
            if (row is null)
                return false;
            var state = ReadCoordination(row, scope, artifactId, CreateId(scope, artifactId));
            if (!StringComparer.Ordinal.Equals(row.IncarnationId, incarnationId))
                return false;
            if (state.Guard is null)
                return true;
            if (GuardIsLive(state, now))
                return false;
            row.ContentJson = RuntimeArtifactJson.Serialize(new CoordinationState(LiveLeases(state, now), null));
            row.SchemaVersion = RuntimeArtifactEfModule.SchemaVersion;
            row.Revision++;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private static Task<WorkflowExecutableCoordinationEntity?> FindCoordinationAsync(
        RuntimeDbContext db,
        string scope,
        string artifactId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var id = CreateId(scope, artifactId);
        var scopeHash = Hash(scope);
        var encodedScope = Encode(scope);
        var artifactIdHash = Hash(artifactId);
        var encodedArtifactId = Encode(artifactId);
        var rows = tracking ? db.WorkflowExecutableCoordinations : db.WorkflowExecutableCoordinations.AsNoTracking();
        return RuntimeArtifactEfPersistenceBoundary.QueryAsync(
            db, "reading", artifactId, () => rows.SingleOrDefaultAsync(
                x => x.Id == id && x.ScopeKeyHash == scopeHash && x.ScopeKey == encodedScope &&
                     x.ArtifactIdHash == artifactIdHash && x.ArtifactId == encodedArtifactId,
                cancellationToken));
    }

    private static async Task<WorkflowExecutableRootWriteLeaseEntity?> FindLeaseAsync(
        RuntimeDbContext db,
        string scope,
        string artifactId,
        string leaseId,
        string id,
        CancellationToken cancellationToken)
    {
        var row = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
            db, "reading", artifactId, () => db.WorkflowExecutableRootWriteLeases.SingleOrDefaultAsync(x => x.Id == id, cancellationToken));
        if (row is null)
            return null;
        if (EfSchemaVersion.NotReadable(RuntimeArtifactEfModule.Chain, row.SchemaVersion) ||
            row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) ||
            row.ArtifactId != Encode(artifactId) || row.ArtifactIdHash != Hash(artifactId) ||
            row.LeaseId != Encode(leaseId) || string.IsNullOrWhiteSpace(row.Token) ||
            string.IsNullOrWhiteSpace(row.IncarnationId) || row.Revision <= 0)
            throw new InvalidDataException("The persisted workflow executable root-write lease row is corrupt.");
        return row;
    }

    private static Task<int> DeleteLeaseAsync(RuntimeDbContext db, string id, string token, CancellationToken cancellationToken) =>
        db.WorkflowExecutableRootWriteLeases
            .Where(x => x.Id == id && x.Token == token)
            .ExecuteDeleteAsync(cancellationToken);

    private static Task<bool> HasLiveLeasesAsync(
        RuntimeDbContext db,
        string scope,
        string artifactId,
        string incarnationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var scopeHash = Hash(scope);
        var encodedScope = Encode(scope);
        var artifactIdHash = Hash(artifactId);
        var encodedArtifactId = Encode(artifactId);
        var nowTicks = now.UtcTicks;
        return db.WorkflowExecutableRootWriteLeases
            .AsNoTracking()
            .AnyAsync(x => x.ScopeKeyHash == scopeHash && x.ScopeKey == encodedScope &&
                           x.ArtifactIdHash == artifactIdHash && x.ArtifactId == encodedArtifactId &&
                           x.IncarnationId == incarnationId && x.ExpiresAtUtcTicks > nowTicks,
                cancellationToken);
    }

    /// <summary>Removes the artifact's expired lease rows and rows of earlier incarnations, which nothing honours.</summary>
    private Task<int> PurgeDeadLeasesAsync(
        string scope,
        string artifactId,
        string incarnationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var scopeHash = Hash(scope);
        var encodedScope = Encode(scope);
        var artifactIdHash = Hash(artifactId);
        var encodedArtifactId = Encode(artifactId);
        var nowTicks = now.UtcTicks;
        return context.WorkflowExecutableRootWriteLeases
            .Where(x => x.ScopeKeyHash == scopeHash && x.ScopeKey == encodedScope &&
                        x.ArtifactIdHash == artifactIdHash && x.ArtifactId == encodedArtifactId &&
                        (x.ExpiresAtUtcTicks <= nowTicks || x.IncarnationId != incarnationId))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static bool GuardIsLive(CoordinationState state, DateTimeOffset now) =>
        state.Guard is { } guard && guard.ExpiresAt > now;

    private static WorkflowExecutableRootWriteLeaseEntity ToLeaseEntity(
        string scope,
        string artifactId,
        string leaseId,
        string id,
        string token,
        DateTimeOffset expiresAt,
        string incarnationId) => new()
        {
            Id = id,
            ScopeKey = Encode(scope),
            ScopeKeyHash = Hash(scope),
            ArtifactId = Encode(artifactId),
            ArtifactIdHash = Hash(artifactId),
            LeaseId = Encode(leaseId),
            Token = token,
            ExpiresAtUtcTicks = expiresAt.UtcTicks,
            IncarnationId = incarnationId,
            Revision = 1,
            SchemaVersion = RuntimeArtifactEfModule.SchemaVersion
        };

    private static WorkflowExecutableEntity ToEntity(
        WorkflowExecutable executable,
        string scope,
        string id,
        string json,
        string incarnationId) => new()
        {
            Id = id,
            ScopeKey = Encode(scope),
            ScopeKeyHash = Hash(scope),
            ArtifactId = Encode(executable.Identity.ArtifactId),
            ArtifactIdHash = Hash(executable.Identity.ArtifactId),
            ArtifactHash = executable.Identity.ArtifactHash,
            ArtifactIdOrderKey = OrderKey(executable.Identity.ArtifactId),
            ContentJson = json,
            SchemaVersion = RuntimeArtifactEfModule.SchemaVersion,
            IncarnationId = incarnationId
        };

    private static WorkflowExecutableCoordinationEntity ToCoordinationEntity(
        string artifactId,
        string scope,
        string id,
        string incarnationId) => new()
        {
            Id = id,
            ScopeKey = Encode(scope),
            ScopeKeyHash = Hash(scope),
            ArtifactId = Encode(artifactId),
            ArtifactIdHash = Hash(artifactId),
            ContentJson = RuntimeArtifactJson.Serialize(CoordinationState.Empty),
            SchemaVersion = RuntimeArtifactEfModule.SchemaVersion,
            Revision = 1,
            IncarnationId = incarnationId
        };

    private static WorkflowExecutable Read(
        WorkflowExecutableEntity row,
        string scope,
        string expected,
        string id)
    {
        if (EfSchemaVersion.NotReadable(RuntimeArtifactEfModule.Chain, row.SchemaVersion) ||
            row.Id != id || row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) ||
            row.ArtifactId != Encode(expected) || row.ArtifactIdHash != Hash(expected) ||
            string.IsNullOrWhiteSpace(row.ArtifactHash) ||
            row.ArtifactHash.Length > RuntimeArtifactEfModule.HashMaximumLength ||
            row.ArtifactIdOrderKey != OrderKey(expected) ||
            string.IsNullOrWhiteSpace(row.IncarnationId))
            throw new InvalidDataException("The persisted workflow executable row is corrupt.");
        try
        {
            var x = RuntimeArtifactJson.Deserialize<WorkflowExecutable>(RuntimeArtifactEfModule.Chain.Upcast<WorkflowExecutableEntity>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]!);
            Validate(x);
            if (x.Identity.ArtifactId != expected || x.Identity.ArtifactHash != row.ArtifactHash)
                throw new InvalidDataException("The persisted workflow executable identity is corrupt.");
            return x;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
                                           ArgumentException or NotSupportedException or NullReferenceException)
        {
            throw new InvalidDataException("The persisted workflow executable payload is corrupt.", exception);
        }
    }

    private static CoordinationState ReadCoordination(WorkflowExecutableCoordinationEntity row, string scope, string expected, string id)
    {
        if (EfSchemaVersion.NotReadable(RuntimeArtifactEfModule.Chain, row.SchemaVersion) ||
            row.Id != id || row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) ||
            row.ArtifactId != Encode(expected) || row.ArtifactIdHash != Hash(expected) ||
            row.Revision <= 0 ||
            string.IsNullOrWhiteSpace(row.IncarnationId))
            throw new InvalidDataException("The persisted workflow executable coordination row is corrupt.");

        try
        {
            var content = RuntimeArtifactEfModule.Chain.Upcast<WorkflowExecutableCoordinationEntity>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]!;
            using var document = JsonDocument.Parse(content);
            EnsureUniqueProperties(document.RootElement);
            var state = RuntimeArtifactJson.Deserialize<CoordinationState>(content);
            ValidateCoordination(state);
            return state;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
                                           ArgumentException or NotSupportedException or FormatException or
                                           OverflowException or NullReferenceException)
        {
            throw new InvalidDataException("The persisted workflow executable coordination payload is corrupt.", exception);
        }
    }

    private static void ValidateCoordination(CoordinationState? state)
    {
        if (state?.Leases is null)
            throw new InvalidDataException("The persisted workflow executable coordination leases are missing.");

        var leaseIds = new HashSet<string>(StringComparer.Ordinal);
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, lease) in state.Leases)
        {
            if (lease is null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(lease.Id) ||
                !StringComparer.Ordinal.Equals(key, lease.Id) || string.IsNullOrWhiteSpace(lease.Token) ||
                lease.ExpiresAt == default || !leaseIds.Add(lease.Id) || !tokens.Add(lease.Token))
            {
                throw new InvalidDataException("The persisted workflow executable coordination contains an invalid lease.");
            }
        }

        if (state.Guard is { } guard &&
            (string.IsNullOrWhiteSpace(guard.OperationId) || string.IsNullOrWhiteSpace(guard.Token) ||
             guard.ExpiresAt == default || !tokens.Add(guard.Token)))
        {
            throw new InvalidDataException("The persisted workflow executable coordination contains an invalid deletion guard.");
        }
    }

    private static void EnsureUniqueProperties(JsonElement element, bool dictionaryKeys = false)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(dictionaryKeys ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException("The persisted workflow executable coordination contains duplicate JSON properties.");
                var isLeaseDictionary = !dictionaryKeys &&
                                        property.Name.Equals("Leases", StringComparison.OrdinalIgnoreCase);
                EnsureUniqueProperties(property.Value, isLeaseDictionary);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                EnsureUniqueProperties(item);
        }
    }

    private static bool CanDelete(CoordinationState state, WorkflowExecutableDeletionGuard guard, DateTimeOffset now) =>
        state.Guard is { } current &&
        current.OperationId == guard.OperationId &&
        current.Token == guard.ConcurrencyToken &&
        current.ExpiresAt > now &&
        state.Leases.All(x => x.Value.ExpiresAt <= now);

    // Privileged access to one partition bypasses the executable cache and lands here (spec 092).
    private string RequireScope() => accessContextAccessor.Current.RequireScope(admitPrivileged: true).Value;

    private static void Validate(WorkflowExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable.Identity.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable.Identity.ArtifactHash);
        if (executable.Identity.ArtifactId.Length > RuntimeArtifactEfModule.IdentityMaximumLength)
            throw new ArgumentOutOfRangeException(nameof(executable));
        if (executable.Identity.ArtifactHash.Length > RuntimeArtifactEfModule.HashMaximumLength)
            throw new ArgumentOutOfRangeException(nameof(executable));
    }

    private static void ValidateTransition(
        string artifact,
        string id,
        DateTimeOffset expiry,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (expiry <= now)
            throw new ArgumentOutOfRangeException(nameof(expiry));
    }
    private static string CreateId(string scope, string artifactId) =>
        Hash($"{scope.Length}:{scope}{artifactId.Length}:{artifactId}");

    private static string CreateLeaseId(string scope, string artifactId, string leaseId) =>
        Hash($"{scope.Length}:{scope}{artifactId.Length}:{artifactId}{leaseId.Length}:{leaseId}");

    private static string NewIncarnationId() => Guid.NewGuid().ToString("N");

    private static string Hash(string x) => EfRelationalIdentity.Hash(x);

    private static string Encode(string x) => EfRelationalIdentity.Encode(x);

    private static string Decode(string x) => EfRelationalIdentity.Decode(x);

    private static string OrderKey(string x) =>
        Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(x, RuntimeArtifactEfModule.IdentityMaximumLength));

    private static string Token() => Guid.NewGuid().ToString("N");

    private static Dictionary<string, Lease> LiveLeases(CoordinationState state, DateTimeOffset now) =>
        state.Leases
            .Where(x => x.Value.ExpiresAt > now)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

    private static InvalidOperationException CoordinationDidNotSettle(string artifactId) =>
        new($"Runtime coordination for workflow executable '{artifactId}' changed concurrently and did not settle after {Coordination.MaxAttempts} attempts.");

    private static RuntimeArtifactEntityFrameworkPersistenceException NormalizeProviderFailure(
        string operation,
        string identity,
        Exception inner) =>
        new(operation, identity, $"The EF runtime artifact store failed while {operation} workflow executable '{identity}'.", inner);

    private static string Encode(string x, string scope) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{Hash(scope)}:{x}"));

    private static string? Decode(string? x, string scope)
    {
        if (x is null)
            return null;
        try
        {
            var value = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(x));
            var prefix = $"{Hash(scope)}:";
            if (!value.StartsWith(prefix, StringComparison.Ordinal))
                throw new FormatException();
            return value[prefix.Length..];
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ArgumentException("The workflow executable continuation token is invalid.", nameof(x), exception);
        }
    }

    private sealed record LeaseGrant(string Token, string IncarnationId, bool Created);

    private sealed record BegunDeletion(WorkflowExecutableDeletionGuard Guard, string IncarnationId, bool Created);

    private sealed record Lease(string Id, string Token, DateTimeOffset ExpiresAt);

    private sealed record Guard(string OperationId, string Token, DateTimeOffset ExpiresAt);

    private sealed record CoordinationState(IReadOnlyDictionary<string, Lease> Leases, Guard? Guard)
    {
        public static CoordinationState Empty =>
            new(new Dictionary<string, Lease>(StringComparer.Ordinal), null);
    }
}
