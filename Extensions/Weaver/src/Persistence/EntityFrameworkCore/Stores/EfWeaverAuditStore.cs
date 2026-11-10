using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Entities;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Services;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// The durable audit sink, reader and ownership: one store whose records and owner associations survive restarts, so
/// owner-scoped audit queries keep answering for recovered sessions. Each record's content is protected; its kind,
/// session, actor and time are plain columns for scoping and order.
/// </summary>
internal sealed class EfWeaverAuditStore(IServiceScopeFactory scopes, Func<IServiceProvider, DbContext> resolveContext, WeaverRecordCodec codec)
    : IWeaverAuditSink, IWeaverAuditReader, IWeaverAuditOwnership
{
    private static readonly EfWriteRetry Retry = new(EfWriteRetry.DefaultMaxAttempts, EfWriteConflict.UniqueKey | EfWriteConflict.Transient);

    public async Task EmitAsync(WeaverAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        cancellationToken.ThrowIfCancellationRequested();
        if (auditEvent.Id.Length > WeaverModelBuilderExtensions.IdentityLength || auditEvent.SessionId?.Length > WeaverModelBuilderExtensions.IdentityLength
            || auditEvent.ActorId?.Length > WeaverModelBuilderExtensions.PrincipalLength)
            throw new ArgumentException("An audit record identity exceeds the durable agent store's limits.", nameof(auditEvent));
        await GuardAsync("append", async () =>
        {
            var payload = codec.Protect(WeaverRecordCodec.AuditPurpose, auditEvent.SessionId ?? "", auditEvent.Id, auditEvent);
            return await Retry.RunUntilSettledAsync<bool>(null, async () =>
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = resolveContext(scope.ServiceProvider);
                try
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                    var sequence = await AllocateAsync(db, cancellationToken);
                    db.Add(new WeaverAuditRow
                    {
                        Sequence = sequence, EventId = auditEvent.Id, Kind = (int)auditEvent.Kind, SessionId = auditEvent.SessionId,
                        ActorId = auditEvent.ActorId, CreatedAtUtcTicks = auditEvent.CreatedAt.UtcTicks, SchemaVersion = WeaverEfModule.SchemaVersion,
                        Payload = payload
                    });
                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return true;
                }
                // Only the first append ever races to create the counter row; it and transient failures are retried.
                catch (Exception exception) when (Retry.ShouldRetry(null, exception)) { return EfWriteAttempt<bool>.Retry(exception); }
            }, conflict => throw new WeaverContributionException("The agent audit store could not append a record: the bounded store retries were exhausted.", conflict),
                cancellationToken);
        });
    }

    /// <summary>
    /// The next append position, allocated under the counter row's lock until the append's transaction ends (a locking
    /// update on SQL Server, PostgreSQL and MySQL; SQLite's write lock). Concurrent appends wait instead of colliding, and
    /// a rolled-back append releases its position. A store without the counter row starts it after the highest recorded
    /// position; a competing first append that created it first fails its key and is retried.
    /// </summary>
    private static async Task<long> AllocateAsync(DbContext db, CancellationToken cancellationToken)
    {
        var counter = db.Set<WeaverAuditSequenceRow>();
        if (await counter.Where(x => x.Id == SequenceRowId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Last, x => x.Last + 1), cancellationToken) == 1)
        {
            var allocated = await counter.AsNoTracking().Where(x => x.Id == SequenceRowId).Select(x => new { x.Last, x.SchemaVersion }).SingleAsync(cancellationToken);
            // A counter a newer module version wrote reports skew; the update rolls back with the append.
            if (allocated.SchemaVersion != WeaverEfModule.SchemaVersion)
                throw new InvalidDataException($"The agent audit sequence schema '{allocated.SchemaVersion}' is not supported by this build.");
            return allocated.Last;
        }
        var next = (await db.Set<WeaverAuditRow>().MaxAsync(x => (long?)x.Sequence, cancellationToken) ?? 0) + 1;
        counter.Add(new WeaverAuditSequenceRow { Id = SequenceRowId, Last = next, SchemaVersion = WeaverEfModule.SchemaVersion });
        return next;
    }

    private const int SequenceRowId = 1;

    public async Task<IReadOnlyCollection<WeaverAuditEvent>> ListAsync(string? sessionId = null, int? take = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await GuardAsync("read", async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = resolveContext(scope.ServiceProvider);
            var rows = db.Set<WeaverAuditRow>().AsNoTracking();
            if (sessionId is not null) rows = rows.Where(x => x.SessionId == sessionId);
            // Newest first; records created at the same time keep their append order, as the default store does.
            var ordered = rows.OrderByDescending(x => x.CreatedAtUtcTicks).ThenBy(x => x.Sequence);
            var page = take is > 0 ? await ordered.Take(take.Value).ToListAsync(cancellationToken) : await ordered.ToListAsync(cancellationToken);
            return (IReadOnlyCollection<WeaverAuditEvent>)page.Select(Decode).ToList();
        });
    }

    public async Task<WeaverAuditQueryResult> ListOwnedAsync(WeaverAuditOwnerQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.ActorId, nameof(query));
        ArgumentException.ThrowIfNullOrWhiteSpace(query.TenantId, nameof(query));
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Take, 1, nameof(query));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Take, WeaverAuditOwnerQuery.MaxTake, nameof(query));
        cancellationToken.ThrowIfCancellationRequested();
        return await GuardAsync("owner read", async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = resolveContext(scope.ServiceProvider);
            // Ownership and the session filter apply before ordering and the limit; ties keep the latest append first.
            // Exact owner match: the suffix keeps a trailing space significant under SQL Server's PAD SPACE comparison.
            var (actor, tenant) = (query.ActorId + ExactSuffix, query.TenantId + ExactSuffix);
            var owned = db.Set<WeaverAuditOwnerRow>().AsNoTracking().Where(x => x.ActorId == query.ActorId && x.TenantId == query.TenantId
                                                                             && x.ActorId + ExactSuffix == actor && x.TenantId + ExactSuffix == tenant);
            var rows = db.Set<WeaverAuditRow>().AsNoTracking().Where(x => x.SessionId != null && owned.Any(owner => owner.SessionId == x.SessionId));
            if (query.SessionId is { } sessionId) rows = rows.Where(x => x.SessionId == sessionId);
            var page = await rows.OrderByDescending(x => x.CreatedAtUtcTicks).ThenByDescending(x => x.Sequence).Take(query.Take).ToListAsync(cancellationToken);
            return new WeaverAuditQueryResult(WeaverAuditQueryOutcome.Succeeded, page.Select(Decode).ToList().AsReadOnly());
        });
    }

    public async Task<bool> AssociateAsync(string sessionId, string actorId, string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();
        if (sessionId.Length > WeaverModelBuilderExtensions.IdentityLength || actorId.Length > WeaverModelBuilderExtensions.PrincipalLength
            || tenantId.Length > WeaverModelBuilderExtensions.PrincipalLength)
            throw new ArgumentException("An audit ownership identity exceeds the durable agent store's limits.", nameof(sessionId));
        return await GuardAsync("ownership association", () => Retry.RunUntilSettledAsync<bool>(null, async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = resolveContext(scope.ServiceProvider);
            try
            {
                // The first association wins: repeating it agrees, a conflicting owner is refused without a change.
                var recorded = await db.Set<WeaverAuditOwnerRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);
                if (recorded is not null)
                    return recorded.ActorId == actorId && recorded.TenantId == tenantId;
                db.Add(new WeaverAuditOwnerRow { SessionId = sessionId, ActorId = actorId, TenantId = tenantId, SchemaVersion = WeaverEfModule.SchemaVersion });
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
            // A competing first association committed; the next attempt reads and compares it.
            catch (Exception exception) when (Retry.ShouldRetry(null, exception)) { return EfWriteAttempt<bool>.Retry(exception); }
        }, conflict => throw new WeaverContributionException(
            $"Agent session '{sessionId}' audit ownership could not be associated: the bounded store retries were exhausted.", conflict), cancellationToken).AsTask());
    }

    /// <summary>
    /// Appended to both sides of an owner comparison so a trailing space stays significant: SQL Server compares
    /// <c>Latin1_General_100_BIN2</c> text with PAD SPACE, and its <c>LEN</c> ignores trailing spaces too.
    /// </summary>
    internal const string ExactSuffix = "|";

    private WeaverAuditEvent Decode(WeaverAuditRow row)
    {
        if (row.SchemaVersion != WeaverEfModule.SchemaVersion)
            throw new InvalidDataException($"The agent audit record schema '{row.SchemaVersion}' is not supported by this build.");
        var value = codec.Unprotect<WeaverAuditEvent>(WeaverRecordCodec.AuditPurpose, row.SessionId ?? "", row.EventId, row.Payload);
        if (value.Id != row.EventId || (int)value.Kind != row.Kind || value.SessionId != row.SessionId || value.ActorId != row.ActorId
            || value.CreatedAt.UtcTicks != row.CreatedAtUtcTicks)
            throw new InvalidDataException("An agent audit record's columns do not match its protected record.");
        return value;
    }

    /// <summary>§2.23.5: provider, codec and serialization failures leave the store wrapped, naming the operation and keeping the cause.</summary>
    private static async Task<T> GuardAsync<T>(string operation, Func<Task<T>> run)
    {
        try
        {
            return await run();
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or ObjectDisposedException or WeaverContributionException))
        {
            throw new WeaverContributionException($"The agent audit store could not complete its {operation}; nothing was changed.", exception);
        }
    }
}
