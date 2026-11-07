using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Elsa.Agent.Persistence.EntityFrameworkCore.Services;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// The durable audit sink, reader and ownership: one store whose records and owner associations survive restarts, so
/// owner-scoped audit queries keep answering for recovered sessions. Each record's content is protected; its kind,
/// session, actor and time are plain columns for scoping and order.
/// </summary>
internal sealed class EfAgentAuditStore(IServiceScopeFactory scopes, Func<IServiceProvider, DbContext> resolveContext, AgentRecordCodec codec)
    : IAgentAuditSink, IAgentAuditReader, IAgentAuditOwnership
{
    private static readonly EfWriteRetry Retry = new(EfWriteRetry.DefaultMaxAttempts, EfWriteConflict.UniqueKey | EfWriteConflict.Transient);

    public async Task EmitAsync(AgentAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        cancellationToken.ThrowIfCancellationRequested();
        if (auditEvent.Id.Length > AgentModelBuilderExtensions.IdentityLength || auditEvent.SessionId?.Length > AgentModelBuilderExtensions.IdentityLength
            || auditEvent.ActorId?.Length > AgentModelBuilderExtensions.PrincipalLength)
            throw new ArgumentException("An audit record identity exceeds the durable agent store's limits.", nameof(auditEvent));
        await GuardAsync("append", async () =>
        {
            return await Retry.RunUntilSettledAsync<bool>(null, async () =>
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = resolveContext(scope.ServiceProvider);
                string? writeVersion = null;
                var saving = false;
                try
                {
                    writeVersion = AgentWriteVersion.Select(db);
                    var payload = codec.Protect(AgentRecordCodec.AuditPurpose, auditEvent.SessionId ?? "", auditEvent.Id, auditEvent);
                    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                    var sequence = await AllocateAsync(db, writeVersion, cancellationToken);
                    db.Add(new AgentAuditRow
                    {
                        Sequence = sequence, EventId = auditEvent.Id, Kind = (int)auditEvent.Kind, SessionId = auditEvent.SessionId,
                        ActorId = auditEvent.ActorId, CreatedAtUtcTicks = auditEvent.CreatedAt.UtcTicks, SchemaVersion = writeVersion,
                        Payload = payload
                    });
                    saving = true;
                    await db.SaveChangesAsync(cancellationToken);
                    saving = false;
                    await transaction.CommitAsync(cancellationToken);
                    return true;
                }
                // Only the first append ever races to create the counter row; it and transient failures are retried.
                catch (Exception exception) when (Retry.ShouldRetry(null, exception) || saving && AgentWriteVersion.IsForwardAdoption(db, writeVersion, exception))
                { return EfWriteAttempt<bool>.Retry(exception); }
            }, conflict => throw new AgentContributionException("The agent audit store could not append a record: the bounded store retries were exhausted.", conflict),
                cancellationToken);
        });
    }

    /// <summary>
    /// The next append position, allocated under the counter row's lock until the append's transaction ends (a locking
    /// update on SQL Server, PostgreSQL and MySQL; SQLite's write lock). Concurrent appends wait instead of colliding, and
    /// a rolled-back append releases its position. A store without the counter row starts it after the highest recorded
    /// position; a competing first append that created it first fails its key and is retried.
    /// </summary>
    private static async Task<long> AllocateAsync(DbContext db, string writeVersion, CancellationToken cancellationToken)
    {
        var counter = db.Set<AgentAuditSequenceRow>();
        if (await counter.Where(x => x.Id == SequenceRowId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Last, x => x.Last + 1), cancellationToken) == 1)
        {
            var allocated = await counter.AsNoTracking().Where(x => x.Id == SequenceRowId).Select(x => new { x.Last, x.SchemaVersion }).SingleAsync(cancellationToken);
            // A counter a newer module version wrote reports skew; the update rolls back with the append.
            EfSchemaVersion.EnsureReadable(AgentEfModule.Chain, allocated.SchemaVersion);
            return allocated.Last;
        }
        var maxima = await db.Set<AgentAuditRow>().AsNoTracking().GroupBy(x => x.SchemaVersion)
            .Select(group => new { SchemaVersion = group.Key, Last = group.Max(x => x.Sequence) }).ToListAsync(cancellationToken);
        foreach (var group in maxima) EfSchemaVersion.EnsureReadable(AgentEfModule.Chain, group.SchemaVersion);
        var next = maxima.Select(x => x.Last).DefaultIfEmpty(0).Max() + 1;
        counter.Add(new AgentAuditSequenceRow { Id = SequenceRowId, Last = next, SchemaVersion = writeVersion });
        return next;
    }

    private const int SequenceRowId = 1;

    public async Task<IReadOnlyCollection<AgentAuditEvent>> ListAsync(string? sessionId = null, int? take = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await GuardAsync("read", async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = resolveContext(scope.ServiceProvider);
            var rows = db.Set<AgentAuditRow>().AsNoTracking();
            if (sessionId is not null) rows = rows.Where(x => x.SessionId == sessionId);
            // Newest first; records created at the same time keep their append order, as the default store does.
            var ordered = rows.OrderByDescending(x => x.CreatedAtUtcTicks).ThenBy(x => x.Sequence);
            var page = take is > 0 ? await ordered.Take(take.Value).ToListAsync(cancellationToken) : await ordered.ToListAsync(cancellationToken);
            return (IReadOnlyCollection<AgentAuditEvent>)page.Select(Decode).ToList();
        });
    }

    public async Task<AgentAuditQueryResult> ListOwnedAsync(AgentAuditOwnerQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.ActorId, nameof(query));
        ArgumentException.ThrowIfNullOrWhiteSpace(query.TenantId, nameof(query));
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Take, 1, nameof(query));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Take, AgentAuditOwnerQuery.MaxTake, nameof(query));
        cancellationToken.ThrowIfCancellationRequested();
        return await GuardAsync("owner read", async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = resolveContext(scope.ServiceProvider);
            // Ownership and the session filter apply before ordering and the limit; ties keep the latest append first.
            // Exact owner match: the suffix keeps a trailing space significant under SQL Server's PAD SPACE comparison.
            var (actor, tenant) = (query.ActorId + ExactSuffix, query.TenantId + ExactSuffix);
            var owned = db.Set<AgentAuditOwnerRow>().AsNoTracking().Where(x => x.ActorId == query.ActorId && x.TenantId == query.TenantId
                                                                             && x.ActorId + ExactSuffix == actor && x.TenantId + ExactSuffix == tenant);
            var rows = db.Set<AgentAuditRow>().AsNoTracking().Where(x => x.SessionId != null && owned.Any(owner => owner.SessionId == x.SessionId));
            if (query.SessionId is { } sessionId) rows = rows.Where(x => x.SessionId == sessionId);
            var page = await rows.OrderByDescending(x => x.CreatedAtUtcTicks).ThenByDescending(x => x.Sequence).Take(query.Take).ToListAsync(cancellationToken);
            var selectedIds = page.Select(x => x.SessionId).Distinct().ToArray();
            var ownerStamps = await owned.Where(x => selectedIds.Contains(x.SessionId)).Select(x => x.SchemaVersion).Distinct().ToListAsync(cancellationToken);
            foreach (var stamp in ownerStamps) EfSchemaVersion.EnsureReadable(AgentEfModule.Chain, stamp);
            return new AgentAuditQueryResult(AgentAuditQueryOutcome.Succeeded, page.Select(Decode).ToList().AsReadOnly());
        });
    }

    public async Task<bool> AssociateAsync(string sessionId, string actorId, string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();
        if (sessionId.Length > AgentModelBuilderExtensions.IdentityLength || actorId.Length > AgentModelBuilderExtensions.PrincipalLength
            || tenantId.Length > AgentModelBuilderExtensions.PrincipalLength)
            throw new ArgumentException("An audit ownership identity exceeds the durable agent store's limits.", nameof(sessionId));
        return await GuardAsync("ownership association", () => Retry.RunUntilSettledAsync<bool>(null, async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = resolveContext(scope.ServiceProvider);
            string? writeVersion = null;
            var saving = false;
            try
            {
                // The first association wins: repeating it agrees, a conflicting owner is refused without a change.
                var recorded = await db.Set<AgentAuditOwnerRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);
                if (recorded is not null)
                {
                    if (recorded.ActorId != actorId || recorded.TenantId != tenantId) return false;
                    EfSchemaVersion.EnsureReadable(AgentEfModule.Chain, recorded.SchemaVersion);
                    return true;
                }
                writeVersion = AgentWriteVersion.Select(db);
                db.Add(new AgentAuditOwnerRow { SessionId = sessionId, ActorId = actorId, TenantId = tenantId, SchemaVersion = writeVersion });
                saving = true;
                await db.SaveChangesAsync(cancellationToken);
                saving = false;
                return true;
            }
            // A competing first association committed; the next attempt reads and compares it.
            catch (Exception exception) when (Retry.ShouldRetry(null, exception) || saving && AgentWriteVersion.IsForwardAdoption(db, writeVersion, exception))
                { return EfWriteAttempt<bool>.Retry(exception); }
        }, conflict => throw new AgentContributionException(
            $"Agent session '{sessionId}' audit ownership could not be associated: the bounded store retries were exhausted.", conflict), cancellationToken).AsTask());
    }

    /// <summary>
    /// Appended to both sides of an owner comparison so a trailing space stays significant: SQL Server compares
    /// <c>Latin1_General_100_BIN2</c> text with PAD SPACE, and its <c>LEN</c> ignores trailing spaces too.
    /// </summary>
    internal const string ExactSuffix = "|";

    private AgentAuditEvent Decode(AgentAuditRow row)
    {
        var content = AgentEfModule.Chain.Upcast<AgentAuditRow>(row.SchemaVersion, (nameof(row.Payload), row.Payload));
        var value = codec.Unprotect<AgentAuditEvent>(AgentRecordCodec.AuditPurpose, row.SessionId ?? "", row.EventId, content[nameof(row.Payload)]!);
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
        catch (Exception exception) when (exception is not (OperationCanceledException or ObjectDisposedException or AgentContributionException
            or EfSchemaVersionSkewException or SchemaWriteRefusedException))
        {
            throw new AgentContributionException($"The agent audit store could not complete its {operation}; nothing was changed.", exception);
        }
    }
}
