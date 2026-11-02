using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Elsa.Agent.Persistence.EntityFrameworkCore.Services;
using Elsa.Persistence.EntityFramework;
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
            var payload = codec.Protect(AgentRecordCodec.AuditPurpose, auditEvent.SessionId ?? "", auditEvent.Id, auditEvent);
            return await Retry.RunUntilSettledAsync<bool>(null, async () =>
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = resolveContext(scope.ServiceProvider);
                try
                {
                    // The next append position; a competing append that took it first fails the key and is retried.
                    var sequence = (await db.Set<AgentAuditRow>().MaxAsync(x => (long?)x.Sequence, cancellationToken) ?? 0) + 1;
                    db.Add(new AgentAuditRow
                    {
                        Sequence = sequence, EventId = auditEvent.Id, Kind = (int)auditEvent.Kind, SessionId = auditEvent.SessionId,
                        ActorId = auditEvent.ActorId, CreatedAtUtcTicks = auditEvent.CreatedAt.UtcTicks, SchemaVersion = AgentEfModule.SchemaVersion,
                        Payload = payload
                    });
                    await db.SaveChangesAsync(cancellationToken);
                    return true;
                }
                catch (Exception exception) when (Retry.ShouldRetry(null, exception)) { return EfWriteAttempt<bool>.Retry(exception); }
            }, conflict => throw new AgentContributionException("The agent audit store could not append a record: the bounded store retries were exhausted.", conflict),
                cancellationToken);
        });
    }

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
            var owned = db.Set<AgentAuditOwnerRow>().AsNoTracking().Where(x => x.ActorId == query.ActorId && x.TenantId == query.TenantId);
            var rows = db.Set<AgentAuditRow>().AsNoTracking().Where(x => x.SessionId != null && owned.Any(owner => owner.SessionId == x.SessionId));
            if (query.SessionId is { } sessionId) rows = rows.Where(x => x.SessionId == sessionId);
            var page = await rows.OrderByDescending(x => x.CreatedAtUtcTicks).ThenByDescending(x => x.Sequence).Take(query.Take).ToListAsync(cancellationToken);
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
            try
            {
                // The first association wins: repeating it agrees, a conflicting owner is refused without a change.
                var recorded = await db.Set<AgentAuditOwnerRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);
                if (recorded is not null)
                    return recorded.ActorId == actorId && recorded.TenantId == tenantId;
                db.Add(new AgentAuditOwnerRow { SessionId = sessionId, ActorId = actorId, TenantId = tenantId, SchemaVersion = AgentEfModule.SchemaVersion });
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
            // A competing first association committed; the next attempt reads and compares it.
            catch (Exception exception) when (Retry.ShouldRetry(null, exception)) { return EfWriteAttempt<bool>.Retry(exception); }
        }, conflict => throw new AgentContributionException(
            $"Agent session '{sessionId}' audit ownership could not be associated: the bounded store retries were exhausted.", conflict), cancellationToken).AsTask());
    }

    private AgentAuditEvent Decode(AgentAuditRow row)
    {
        if (row.SchemaVersion != AgentEfModule.SchemaVersion)
            throw new InvalidDataException($"The agent audit record schema '{row.SchemaVersion}' is not supported by this build.");
        var value = codec.Unprotect<AgentAuditEvent>(AgentRecordCodec.AuditPurpose, row.SessionId ?? "", row.EventId, row.Payload);
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
        catch (Exception exception) when (exception is not (OperationCanceledException or ObjectDisposedException or AgentContributionException))
        {
            throw new AgentContributionException($"The agent audit store could not complete its {operation}; nothing was changed.", exception);
        }
    }
}
