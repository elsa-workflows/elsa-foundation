using System.Globalization;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Entities;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Services;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// The durable session record store. Every operation runs in a fresh owned context. A write or acquisition first moves
/// the session row's version (a write only under its owner and fence), which takes the aggregate's row lock until
/// commit and so serializes its writers on every provider; it then checks keys and proposal versions and applies
/// everything in the same transaction. Leases are measured in database time. Payload columns hold ciphertext only.
/// </summary>
internal sealed partial class EfAgentSessionRecordStore(
    IServiceScopeFactory scopes,
    Func<IServiceProvider, DbContext> resolveContext,
    AgentRecordCodec codec,
    TimeSpan leaseDuration,
    IAgentStoreClock? clock = null) : IAgentSessionRecordStore
{
    private static readonly EfWriteRetry Retry = new(EfWriteRetry.DefaultMaxAttempts,
        EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey | EfWriteConflict.Transient);
    private const int MaximumReadAttempts = 8;
    private readonly IAgentStoreClock _clock = clock ?? AgentDatabaseClock.Instance;

    public bool SupportsFencing => true;

    public async Task<AgentSessionRecordResult> CreateAsync(AgentSessionRecord record, string ownerToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerToken);
        cancellationToken.ThrowIfCancellationRequested();
        var sessionId = record.Session.Id;
        // Encode before any database work; a value that cannot be recorded refuses the create before any change.
        var encoded = EncodeOrRefuse(sessionId, "create", new AgentSessionRecordWrite(sessionId, new(ownerToken, 0))
        {
            Session = record.Session, Messages = record.Messages, Snapshots = record.Snapshots, Context = record.Context,
            PausedTurn = record.PausedTurn, Proposals = record.Proposals, Keys = record.Keys, PendingAudits = record.PendingAudits
        }, refuseDuplicateContext: true);
        return await GuardRetryAsync(sessionId, "create", () => Retry.RunUntilSettledAsync<AgentSessionRecordResult>(null, async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = resolveContext(scope.ServiceProvider);
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var proposalIds = encoded.Proposals.Select(x => x.Id).ToArray();
                if (await db.Set<AgentSessionRow>().AnyAsync(x => x.SessionId == sessionId, cancellationToken)
                    || proposalIds.Length > 0 && await db.Set<AgentProposalRow>().AnyAsync(x => proposalIds.Contains(x.ProposalId), cancellationToken))
                    return new AgentSessionRecordResult(AgentSessionRecordOutcome.AlreadyExists);
                var now = await _clock.NowAsync(db, cancellationToken);
                var sequence = 0L;
                var session = encoded.Session!;
                session.OwnerToken = ownerToken;
                session.Fence = 1;
                session.LeaseExpiresAtUtcTicks = (now + leaseDuration).UtcTicks;
                session.Version = 1;
                session.PausedTurnPayload = encoded.PausedTurn;
                session.PendingAuditsPayload = encoded.PendingAudits;
                db.Add(session);
                foreach (var message in encoded.Messages) db.Add(message.Row(sessionId, sequence++));
                foreach (var snapshot in encoded.Snapshots) db.Add(snapshot.Row(sessionId, sequence++));
                foreach (var attachment in encoded.Context) db.Add(attachment.Row(sessionId, sequence++));
                foreach (var proposal in encoded.Proposals) db.Add(proposal.Row(sessionId, sequence++, proposal.Occurrences));
                foreach (var key in encoded.Keys) db.Add(key.Row(sessionId, sequence++));
                session.NextSequence = sequence;
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Owned(new(ownerToken, 1), session.LeaseExpiresAtUtcTicks);
            }
            // A competing create committed the same session or proposal identity first; the next attempt reads it.
            catch (Exception exception) when (Retry.ShouldRetry(null, exception)) { return EfWriteAttempt<AgentSessionRecordResult>.Retry(exception); }
        }, Exhausted<AgentSessionRecordResult>(sessionId, "create"), cancellationToken));
    }

    public async Task<AgentSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await GuardAsync(sessionId, "load", async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = resolveContext(scope.ServiceProvider);
            for (var attempt = 0; attempt < MaximumReadAttempts; attempt++)
            {
                var session = await db.Set<AgentSessionRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);
                if (session is null) return null;
                var read = new LoadedRows(session,
                    await db.Set<AgentMessageRow>().AsNoTracking().Where(x => x.SessionId == sessionId).OrderBy(x => x.Sequence).ToListAsync(cancellationToken),
                    await db.Set<AgentDisclosureSnapshotRow>().AsNoTracking().Where(x => x.SessionId == sessionId).OrderBy(x => x.Sequence).ToListAsync(cancellationToken),
                    await db.Set<AgentContextRow>().AsNoTracking().Where(x => x.SessionId == sessionId).OrderBy(x => x.Sequence).ToListAsync(cancellationToken),
                    await db.Set<AgentProposalRow>().AsNoTracking().Where(x => x.SessionId == sessionId).OrderBy(x => x.Sequence).ToListAsync(cancellationToken),
                    await db.Set<AgentCallKeyRow>().AsNoTracking().Where(x => x.SessionId == sessionId).OrderBy(x => x.Sequence).ToListAsync(cancellationToken));
                // Every write moves the session version, so an unchanged version proves one consistent aggregate.
                var version = await db.Set<AgentSessionRow>().AsNoTracking().Where(x => x.SessionId == sessionId)
                    .Select(x => (long?)x.Version).SingleOrDefaultAsync(cancellationToken);
                if (version == session.Version) return read;
            }
            throw new AgentContributionException($"Agent session '{sessionId}' kept changing while it was read; nothing was hydrated.");
        });
        if (rows is null) return null;
        try
        {
            return Decode(sessionId, rows);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidCastException or ArgumentException or FormatException or OverflowException)
        {
            // FR-018: unreadable, corrupt or foreign recorded state makes the session unavailable; nothing was changed.
            throw new AgentSessionUnavailableException(sessionId, "unreadable", exception);
        }
    }

    public async Task<AgentSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await GuardRetryAsync(sessionId, "ownership acquisition", () => Retry.RunUntilSettledAsync<AgentSessionRecordResult>(null, async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = resolveContext(scope.ServiceProvider);
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                // Moving the version takes the aggregate's row lock until commit: competing writers wait rather than race.
                if (await db.Set<AgentSessionRow>().Where(x => x.SessionId == sessionId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Version, x => x.Version + 1), cancellationToken) != 1)
                    return new AgentSessionRecordResult(AgentSessionRecordOutcome.NotFound);
                var row = await db.Set<AgentSessionRow>().AsNoTracking().Where(x => x.SessionId == sessionId)
                    .Select(x => new { x.OwnerToken, x.Fence, x.LeaseExpiresAtUtcTicks }).SingleAsync(cancellationToken);
                var now = await _clock.NowAsync(db, cancellationToken);
                // A refusal returns with the transaction uncommitted, so it changes nothing.
                if (row.OwnerToken != ownerToken && row.LeaseExpiresAtUtcTicks > now.UtcTicks)
                    return new AgentSessionRecordResult(AgentSessionRecordOutcome.OwnedElsewhere) { LeaseExpiresAt = Time(row.LeaseExpiresAtUtcTicks) };
                // Only a takeover moves the fence; the current owner re-acquiring its own lapsed lease keeps it.
                var fence = row.OwnerToken == ownerToken ? row.Fence : checked(row.Fence + 1);
                var lease = (now + leaseDuration).UtcTicks;
                await db.Set<AgentSessionRow>().Where(x => x.SessionId == sessionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.OwnerToken, ownerToken).SetProperty(x => x.Fence, fence)
                        .SetProperty(x => x.LeaseExpiresAtUtcTicks, lease), cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Owned(new(ownerToken, fence), lease);
            }
            catch (Exception exception) when (Retry.ShouldRetry(null, exception)) { return EfWriteAttempt<AgentSessionRecordResult>.Retry(exception); }
        }, Exhausted<AgentSessionRecordResult>(sessionId, "ownership acquisition"), cancellationToken));
    }

    public async Task ReleaseOwnershipAsync(string sessionId, AgentSessionFence fence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(fence);
        cancellationToken.ThrowIfCancellationRequested();
        await GuardRetryAsync(sessionId, "ownership release", () => Retry.RunUntilSettledAsync<bool>(null, async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = resolveContext(scope.ServiceProvider);
            try
            {
                // One conditional statement: a fence that is no longer current releases nothing.
                await db.Set<AgentSessionRow>().Where(x => x.SessionId == sessionId && x.OwnerToken == fence.OwnerToken && x.Fence == fence.Fence)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LeaseExpiresAtUtcTicks, (long?)null)
                        .SetProperty(x => x.Version, x => x.Version + 1), cancellationToken);
                return true;
            }
            catch (Exception exception) when (Retry.ShouldRetry(null, exception)) { return EfWriteAttempt<bool>.Retry(exception); }
        }, Exhausted<bool>(sessionId, "ownership release"), cancellationToken));
    }

    public async Task<AgentSessionRecordResult> WriteAsync(AgentSessionRecordWrite write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        cancellationToken.ThrowIfCancellationRequested();
        var sessionId = write.SessionId;
        // Encode outside the transaction; a value that cannot be recorded refuses the write before any change.
        var encoded = EncodeOrRefuse(sessionId, "write", write, refuseDuplicateContext: false);
        return await GuardRetryAsync(sessionId, "write", () => Retry.RunUntilSettledAsync<AgentSessionRecordResult>(null, async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = resolveContext(scope.ServiceProvider);
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                // Moving the version under the writer's owner and fence takes the aggregate's row lock until commit, so
                // competing writers of the session wait for this one, and the reads below cannot change before it ends.
                // Guarded on owner and fence only: an unchallenged lapsed lease is re-acquired by this same write.
                var locked = await db.Set<AgentSessionRow>()
                    .Where(x => x.SessionId == sessionId && x.OwnerToken == write.Fence.OwnerToken && x.Fence == write.Fence.Fence)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Version, x => x.Version + 1), cancellationToken);
                if (locked != 1)
                    return new AgentSessionRecordResult(await db.Set<AgentSessionRow>().AnyAsync(x => x.SessionId == sessionId, cancellationToken)
                        ? AgentSessionRecordOutcome.OwnershipLost
                        : AgentSessionRecordOutcome.NotFound);
                var row = await db.Set<AgentSessionRow>().AsNoTracking().Where(x => x.SessionId == sessionId)
                    .Select(x => new { x.Version, x.NextSequence }).SingleAsync(cancellationToken);

                var hashes = encoded.Keys.Select(x => x.Hash).Distinct().ToArray();
                var recordedKeys = hashes.Length == 0 ? [] : await db.Set<AgentCallKeyRow>().AsNoTracking()
                    .Where(x => x.SessionId == sessionId && hashes.Contains(x.KeyHash)).ToListAsync(cancellationToken);
                var conflicts = write.Keys
                    .Select(key => (Key: key, Recorded: recordedKeys.SingleOrDefault(x => x.Kind == key.Kind && x.KeyHash == AgentRecordCodec.Hash(key.Key))))
                    .Where(x => x.Recorded is not null && x.Recorded.Value != x.Key.Value)
                    .Select(x => new AgentSessionUniqueKey(x.Key.Kind, x.Key.Key, x.Recorded!.Value)).ToArray();
                if (conflicts.Length > 0)
                    return new AgentSessionRecordResult(AgentSessionRecordOutcome.KeyConflict) { ConflictingKeys = conflicts };

                var proposalIds = encoded.Proposals.Select(x => x.Id).ToArray();
                var recordedProposals = proposalIds.Length == 0 ? [] : await db.Set<AgentProposalRow>()
                    .Where(x => proposalIds.Contains(x.ProposalId)).ToListAsync(cancellationToken);
                if (recordedProposals.Any(x => x.SessionId != sessionId))
                    return new AgentSessionRecordResult(AgentSessionRecordOutcome.AlreadyExists);
                // Compare-and-set: each proposal record changes only from the version its writer read (0: not yet recorded).
                if (encoded.Proposals.Any(proposal => proposal.ReadVersions.Any(version =>
                        version != (recordedProposals.SingleOrDefault(x => x.ProposalId == proposal.Id)?.Version ?? 0))))
                    return new AgentSessionRecordResult(AgentSessionRecordOutcome.VersionConflict);

                var now = await _clock.NowAsync(db, cancellationToken);
                var sequence = row.NextSequence;
                var messageIds = encoded.Messages.Select(x => x.Id).ToArray();
                var recordedMessages = messageIds.Length == 0 ? [] : await db.Set<AgentMessageRow>()
                    .Where(x => x.SessionId == sessionId && messageIds.Contains(x.MessageId)).ToListAsync(cancellationToken);
                foreach (var message in encoded.Messages)
                {
                    if (recordedMessages.SingleOrDefault(x => x.MessageId == message.Id) is { } recorded) message.Apply(recorded);
                    else db.Add(message.Row(sessionId, sequence++));
                }
                // Each distinct snapshot is recorded once; a digest already recorded keeps its recorded text.
                var digests = encoded.Snapshots.Select(x => x.Digest).ToArray();
                var recordedDigests = digests.Length == 0 ? [] : await db.Set<AgentDisclosureSnapshotRow>().AsNoTracking()
                    .Where(x => x.SessionId == sessionId && digests.Contains(x.Digest)).Select(x => x.Digest).ToListAsync(cancellationToken);
                foreach (var snapshot in encoded.Snapshots.Where(x => !recordedDigests.Contains(x.Digest, StringComparer.Ordinal)))
                    db.Add(snapshot.Row(sessionId, sequence++));
                // Context is appended by attachment identity; an identity already recorded is kept unchanged.
                var attachmentKeys = encoded.Context.Select(x => x.Key).ToArray();
                var recordedAttachments = attachmentKeys.Length == 0 ? [] : await db.Set<AgentContextRow>().AsNoTracking()
                    .Where(x => x.SessionId == sessionId && attachmentKeys.Contains(x.AttachmentKey)).Select(x => x.AttachmentKey).ToListAsync(cancellationToken);
                foreach (var attachment in encoded.Context.Where(x => !recordedAttachments.Contains(x.Key, StringComparer.Ordinal)))
                    db.Add(attachment.Row(sessionId, sequence++));
                foreach (var proposal in encoded.Proposals)
                {
                    if (recordedProposals.SingleOrDefault(x => x.ProposalId == proposal.Id) is { } recorded) proposal.Apply(recorded);
                    else db.Add(proposal.Row(sessionId, sequence++, proposal.Occurrences));
                }
                foreach (var key in encoded.Keys.Where(key => !recordedKeys.Any(x => x.Kind == key.Kind && x.KeyHash == key.Hash)))
                    db.Add(key.Row(sessionId, sequence++));
                await db.SaveChangesAsync(cancellationToken);

                var lease = (now + leaseDuration).UtcTicks;
                var session = encoded.Session;
                var paused = encoded.PausedTurn;
                var removePaused = paused is null && write.RemovePausedTurn;
                var (writesPendingAudits, pendingAudits) = (encoded.WritesPendingAudits, encoded.PendingAudits);
                var next = sequence;
                var applied = await db.Set<AgentSessionRow>().Where(x => x.SessionId == sessionId && x.Version == row.Version)
                    .ExecuteUpdateAsync(setters =>
                    {
                        setters.SetProperty(x => x.LeaseExpiresAtUtcTicks, lease).SetProperty(x => x.NextSequence, next);
                        if (session is not null)
                            setters.SetProperty(x => x.TenantId, session.TenantId).SetProperty(x => x.ActorId, session.ActorId)
                                .SetProperty(x => x.Workspace, session.Workspace).SetProperty(x => x.Title, session.Title)
                                .SetProperty(x => x.Status, session.Status).SetProperty(x => x.UpdatedAtUtcTicks, session.UpdatedAtUtcTicks)
                                .SetProperty(x => x.ExpiresAtUtcTicks, session.ExpiresAtUtcTicks).SetProperty(x => x.PolicyRevision, session.PolicyRevision)
                                .SetProperty(x => x.ContinuedFrom, session.ContinuedFrom).SetProperty(x => x.LimitReached, session.LimitReached)
                                .SetProperty(x => x.Payload, session.Payload);
                        if (paused is not null) setters.SetProperty(x => x.PausedTurnPayload, paused);
                        else if (removePaused) setters.SetProperty(x => x.PausedTurnPayload, (string?)null);
                        if (writesPendingAudits) setters.SetProperty(x => x.PendingAuditsPayload, pendingAudits);
                    }, cancellationToken);
                if (applied != 1)
                    throw new InvalidOperationException($"Agent session '{sessionId}' changed while this write held its row lock.");
                await transaction.CommitAsync(cancellationToken);
                return Owned(write.Fence, lease);
            }
            // A racing insert of the same proposal identity by another session, or a transient lock conflict; the
            // transaction was disposed uncommitted, so the next attempt re-evaluates everything from a fresh read.
            catch (Exception exception) when (Retry.ShouldRetry(null, exception)) { return EfWriteAttempt<AgentSessionRecordResult>.Retry(exception); }
        }, Exhausted<AgentSessionRecordResult>(sessionId, "write"), cancellationToken));
    }

    public async Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposalId);
        cancellationToken.ThrowIfCancellationRequested();
        return await GuardAsync(null, "proposal lookup", async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = resolveContext(scope.ServiceProvider);
            return await db.Set<AgentProposalRow>().AsNoTracking().Where(x => x.ProposalId == proposalId).Select(x => x.SessionId)
                .SingleOrDefaultAsync(cancellationToken);
        });
    }

    public async Task<AgentSessionSummaryPage> ListOwnedAsync(AgentSessionOwnerQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.ActorId, nameof(query));
        ArgumentException.ThrowIfNullOrWhiteSpace(query.TenantId, nameof(query));
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Take, 1, nameof(query));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Take, AgentSessionOwnerQuery.MaxTake, nameof(query));
        cancellationToken.ThrowIfCancellationRequested();
        var before = Cursor.Parse(query.Before);
        var awaiting = (int)AgentActionProposalStatus.AwaitingApproval;
        var edited = (int)AgentActionProposalStatus.Edited;
        return await GuardAsync(null, "owner list", async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = resolveContext(scope.ServiceProvider);
            // Store time, as leases use: a review past its prepared deadline is not counted and an expired session is listed closed.
            var now = (await _clock.NowAsync(db, cancellationToken)).UtcTicks;
            // Owner first, then workspace, then order, cursor and limit; payloads are never decoded.
            // Exact owner and workspace match: the suffix keeps a trailing space significant under SQL Server's PAD SPACE comparison.
            var (actor, tenant) = (query.ActorId + EfAgentAuditStore.ExactSuffix, query.TenantId + EfAgentAuditStore.ExactSuffix);
            var sessions = db.Set<AgentSessionRow>().AsNoTracking().Where(x => x.ActorId == query.ActorId && x.TenantId == query.TenantId
                                                                             && x.ActorId + EfAgentAuditStore.ExactSuffix == actor
                                                                             && x.TenantId + EfAgentAuditStore.ExactSuffix == tenant);
            if (query.Workspace is { } workspace)
            {
                var exactWorkspace = workspace + EfAgentAuditStore.ExactSuffix;
                sessions = sessions.Where(x => x.Workspace == workspace && x.Workspace + EfAgentAuditStore.ExactSuffix == exactWorkspace);
            }
            if (before is { } cursor)
                sessions = sessions.Where(x => x.UpdatedAtUtcTicks < cursor.Ticks
                                               || x.UpdatedAtUtcTicks == cursor.Ticks && string.Compare(x.SessionId, cursor.Id) < 0);
            var page = await sessions.OrderByDescending(x => x.UpdatedAtUtcTicks).ThenByDescending(x => x.SessionId).Take(query.Take + 1)
                .Select(x => new
                {
                    x.SessionId, x.Title, x.Status, x.UpdatedAtUtcTicks, x.ExpiresAtUtcTicks, x.LimitReached,
                    Pending = db.Set<AgentProposalRow>().Count(p => p.SessionId == x.SessionId && (p.Status == awaiting || p.Status == edited)
                                                                     && (p.ExpiresAtUtcTicks == null || p.ExpiresAtUtcTicks > now))
                }).ToListAsync(cancellationToken);
            var items = page.Take(query.Take)
                .Select(x => new AgentSessionSummary(x.SessionId, x.Title,
                    (AgentSessionStatus)x.Status == AgentSessionStatus.Active && x.ExpiresAtUtcTicks <= now ? AgentSessionStatus.Expired : (AgentSessionStatus)x.Status,
                    Time(x.UpdatedAtUtcTicks),
                    x.ExpiresAtUtcTicks is { } expires ? Time(expires) : null, x.Pending, AgentSessionRecoveryStates.Of(x.LimitReached)))
                .ToArray();
            var next = page.Count > query.Take ? Cursor.Format(items[^1].UpdatedAt, items[^1].Id) : null;
            return new AgentSessionSummaryPage(items, next);
        });
    }

    public async Task<AgentSessionOwner?> FindOwnerAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        // The plain owner columns only: no payload is read or decoded, and nothing is written.
        return await GuardAsync(sessionId, "owner read", async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = resolveContext(scope.ServiceProvider);
            return await db.Set<AgentSessionRow>().AsNoTracking().Where(x => x.SessionId == sessionId)
                .Select(x => new AgentSessionOwner(x.ActorId, x.TenantId)).SingleOrDefaultAsync(cancellationToken);
        });
    }

    /// <summary>A value that cannot be serialized or protected refuses the operation before any change, wrapped (§2.23.5).</summary>
    private Encoded EncodeOrRefuse(string sessionId, string operation, AgentSessionRecordWrite write, bool refuseDuplicateContext)
    {
        try
        {
            return Encode(sessionId, write, refuseDuplicateContext);
        }
        catch (Exception exception) when (exception is not (ArgumentException or OperationCanceledException))
        {
            throw new AgentContributionException($"Agent session '{sessionId}' could not encode its {operation}; nothing was changed.", exception);
        }
    }

    private static AgentSessionRecordResult Owned(AgentSessionFence fence, long? leaseTicks)
        => new(AgentSessionRecordOutcome.Succeeded) { Fence = fence, LeaseExpiresAt = leaseTicks is { } ticks ? Time(ticks) : null };

    private static DateTimeOffset? Time(long? ticks) => ticks is { } value ? Time(value) : null;
    private static DateTimeOffset Time(long ticks) => new(ticks, TimeSpan.Zero);

    private static Func<Exception?, ValueTask<T>> Exhausted<T>(string sessionId, string operation) => conflict =>
        throw new AgentContributionException($"Agent session '{sessionId}' could not complete its {operation}: the bounded store retries were exhausted; nothing was changed.", conflict);

    /// <summary>
    /// §2.23.5: a provider or infrastructure failure leaves this store as an <see cref="AgentContributionException"/> that
    /// names the session and operation and keeps the cause. Cancellation, a disposed host, and the contract's own argument
    /// and unavailable-state refusals keep their own type.
    /// </summary>
    private static async Task<T> GuardAsync<T>(string? sessionId, string operation, Func<Task<T>> run)
    {
        try
        {
            return await run();
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or ObjectDisposedException or AgentContributionException
                                              or AgentSessionUnavailableException))
        {
            var subject = sessionId is null ? "The agent session record store" : $"Agent session '{sessionId}'";
            throw new AgentContributionException($"{subject} could not complete its {operation}; nothing was changed.", exception);
        }
    }

    private static Task<T> GuardRetryAsync<T>(string? sessionId, string operation, Func<ValueTask<T>> run)
        => GuardAsync(sessionId, operation, () => run().AsTask());

    private static class Cursor
    {
        public static string Format(DateTimeOffset updatedAt, string id)
            => updatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture) + ":" + id;

        public static (long Ticks, string Id)? Parse(string? value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var separator = value.IndexOf(':');
            if (separator <= 0 || !long.TryParse(value.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
                throw new ArgumentException("The session list cursor is not one this store issued.", nameof(value));
            return (ticks, value[(separator + 1)..]);
        }
    }
}
