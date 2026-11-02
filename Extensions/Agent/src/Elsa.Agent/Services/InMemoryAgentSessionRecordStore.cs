using System.Globalization;
using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Services;

/// <summary>
/// The default process-lifetime record store. Every part of an aggregate is held as serialized JSON: a write
/// serializes what it records and a load deserializes a fresh copy, so an old and a new host over one store never
/// share a mutable object. Owner, fence and lease are enforced exactly as the contract states.
/// </summary>
internal sealed class InMemoryAgentSessionRecordStore(TimeProvider? clock = null) : IAgentSessionRecordStore
{
    /// <summary>How long a write or acquisition keeps the owner's lease live.</summary>
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    // Default options keep the canonical shape the integrity digests read.
    private static readonly JsonSerializerOptions JsonOptions = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly Dictionary<string, Aggregate> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _proposalSessions = new(StringComparer.Ordinal);

    public bool SupportsFencing => true;

    public Task<AgentSessionRecordResult> CreateAsync(AgentSessionRecord record, string ownerToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerToken);
        cancellationToken.ThrowIfCancellationRequested();
        var created = new Aggregate(Serialize(record.Session), Index.From(record.Session));
        foreach (var message in record.Messages) created.Messages.Upsert(message.Id, Serialize(message));
        foreach (var attachment in record.Context) created.Context.Add(attachment.Id, Serialize(attachment));
        created.PausedTurn = record.PausedTurn is null ? null : Serialize(record.PausedTurn);
        foreach (var proposal in record.Proposals) created.Proposals.Upsert(proposal.Proposal.Id, Serialize(proposal), proposal.Proposal.Status);
        foreach (var key in record.Keys) created.Keys[(key.Kind, key.Key)] = key.Value;
        lock (_gate)
        {
            if (_sessions.ContainsKey(record.Session.Id) || record.Proposals.Any(x => _proposalSessions.ContainsKey(x.Proposal.Id)))
                return Task.FromResult(new AgentSessionRecordResult(AgentSessionRecordOutcome.AlreadyExists));
            var now = _clock.GetUtcNow();
            created.Owner = new(ownerToken, 1);
            created.LeaseExpiresAt = now + LeaseDuration;
            created.Version = 1;
            _sessions.Add(record.Session.Id, created);
            foreach (var proposal in record.Proposals) _proposalSessions[proposal.Proposal.Id] = record.Session.Id;
            return Task.FromResult(Owned(created));
        }
    }

    public Task<AgentSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var aggregate)) return Task.FromResult<AgentSessionRecord?>(null);
            return Task.FromResult<AgentSessionRecord?>(new AgentSessionRecord(Deserialize<AgentSession>(aggregate.Session))
            {
                Messages = aggregate.Messages.Values.Select(Deserialize<AgentMessage>).ToArray(),
                Context = aggregate.Context.Values.Select(Deserialize<AgentContextAttachment>).ToArray(),
                PausedTurn = aggregate.PausedTurn is null ? null : Deserialize<AgentPausedTurn>(aggregate.PausedTurn),
                Proposals = aggregate.Proposals.Values.Select(Deserialize<AgentProposalRecord>).ToArray(),
                Keys = aggregate.Keys.Select(x => new AgentSessionUniqueKey(x.Key.Kind, x.Key.Key, x.Value)).ToArray(),
                Owner = aggregate.Owner,
                LeaseExpiresAt = aggregate.LeaseExpiresAt,
                Version = aggregate.Version
            });
        }
    }

    public Task<AgentSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerToken);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var aggregate))
                return Task.FromResult(new AgentSessionRecordResult(AgentSessionRecordOutcome.NotFound));
            var now = _clock.GetUtcNow();
            if (aggregate.Owner is { } current && current.OwnerToken != ownerToken && aggregate.LeaseExpiresAt > now)
                return Task.FromResult(new AgentSessionRecordResult(AgentSessionRecordOutcome.OwnedElsewhere) { LeaseExpiresAt = aggregate.LeaseExpiresAt });
            // Only a takeover moves the fence; the current owner re-acquiring its own lapsed lease keeps it.
            aggregate.Owner = aggregate.Owner is { } owned && owned.OwnerToken == ownerToken ? owned : new(ownerToken, (aggregate.Owner?.Fence ?? 0) + 1);
            aggregate.LeaseExpiresAt = now + LeaseDuration;
            aggregate.Version++;
            return Task.FromResult(Owned(aggregate));
        }
    }

    public Task ReleaseOwnershipAsync(string sessionId, AgentSessionFence fence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fence);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            if (_sessions.TryGetValue(sessionId, out var aggregate) && aggregate.Owner == fence)
            {
                aggregate.LeaseExpiresAt = null;
                aggregate.Version++;
            }
        return Task.CompletedTask;
    }

    public Task<AgentSessionRecordResult> WriteAsync(AgentSessionRecordWrite write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        cancellationToken.ThrowIfCancellationRequested();
        // Serialize outside the store lock; a value that cannot be recorded refuses the write before any change.
        var session = write.Session is null ? null : Serialize(write.Session);
        var messages = write.Messages.Select(x => (x.Id, Json: Serialize(x))).ToArray();
        var context = write.Context.Select(x => (x.Id, Json: Serialize(x))).ToArray();
        var paused = write.PausedTurn is null ? null : Serialize(write.PausedTurn);
        var proposals = write.Proposals.Select(x => (x.Proposal.Id, x.Proposal.Status, Json: Serialize(x))).ToArray();
        lock (_gate)
        {
            if (!_sessions.TryGetValue(write.SessionId, out var aggregate))
                return Task.FromResult(new AgentSessionRecordResult(AgentSessionRecordOutcome.NotFound));
            // Guarded on owner and fence only: an unchallenged lapsed lease is re-acquired by this same write.
            if (aggregate.Owner != write.Fence)
                return Task.FromResult(new AgentSessionRecordResult(AgentSessionRecordOutcome.OwnershipLost));
            var conflicts = write.Keys.Where(x => aggregate.Keys.TryGetValue((x.Kind, x.Key), out var value) && value != x.Value)
                .Select(x => new AgentSessionUniqueKey(x.Kind, x.Key, aggregate.Keys[(x.Kind, x.Key)])).ToArray();
            if (conflicts.Length > 0)
                return Task.FromResult(new AgentSessionRecordResult(AgentSessionRecordOutcome.KeyConflict) { ConflictingKeys = conflicts });
            if (proposals.Any(x => _proposalSessions.TryGetValue(x.Id, out var owner) && owner != write.SessionId))
                return Task.FromResult(new AgentSessionRecordResult(AgentSessionRecordOutcome.AlreadyExists));

            if (session is not null)
            {
                aggregate.Session = session;
                aggregate.Index = Index.From(write.Session!);
            }
            foreach (var (id, json) in messages) aggregate.Messages.Upsert(id, json);
            foreach (var (id, json) in context) aggregate.Context.TryAdd(id, json);
            if (paused is not null) aggregate.PausedTurn = paused;
            else if (write.RemovePausedTurn) aggregate.PausedTurn = null;
            foreach (var (id, status, json) in proposals)
            {
                aggregate.Proposals.Upsert(id, json, status);
                _proposalSessions[id] = write.SessionId;
            }
            foreach (var key in write.Keys) aggregate.Keys.TryAdd((key.Kind, key.Key), key.Value);
            aggregate.LeaseExpiresAt = _clock.GetUtcNow() + LeaseDuration;
            aggregate.Version++;
            return Task.FromResult(Owned(aggregate));
        }
    }

    public Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_proposalSessions.GetValueOrDefault(proposalId));
    }

    public Task<AgentSessionSummaryPage> ListOwnedAsync(AgentSessionOwnerQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.ActorId, nameof(query));
        ArgumentException.ThrowIfNullOrWhiteSpace(query.TenantId, nameof(query));
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Take, 1, nameof(query));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Take, AgentSessionOwnerQuery.MaxTake, nameof(query));
        cancellationToken.ThrowIfCancellationRequested();
        var before = Cursor.Parse(query.Before);
        lock (_gate)
        {
            // Owner first, then workspace, then order, cursor and limit; payloads are never decoded.
            var page = _sessions
                .Select(x => (Id: x.Key, x.Value.Index, Pending: x.Value.Proposals.PendingReviewCount))
                .Where(x => x.Index.ActorId == query.ActorId && x.Index.TenantId == query.TenantId)
                .Where(x => query.Workspace is null || x.Index.Workspace == query.Workspace)
                .OrderByDescending(x => x.Index.UpdatedAt).ThenByDescending(x => x.Id, StringComparer.Ordinal)
                .Where(x => before is not { } cursor || Cursor.Precedes(cursor, x.Index.UpdatedAt, x.Id))
                .Take(query.Take + 1)
                .ToList();
            var items = page.Take(query.Take)
                .Select(x => new AgentSessionSummary(x.Id, x.Index.Title, x.Index.Status, x.Index.UpdatedAt, x.Index.ExpiresAt, x.Pending, null))
                .ToArray();
            var next = page.Count > query.Take ? Cursor.Format(items[^1].UpdatedAt, items[^1].Id) : null;
            return Task.FromResult(new AgentSessionSummaryPage(items, next));
        }
    }

    private static AgentSessionRecordResult Owned(Aggregate aggregate)
        => new(AgentSessionRecordOutcome.Succeeded) { Fence = aggregate.Owner, LeaseExpiresAt = aggregate.LeaseExpiresAt };

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static T Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new JsonException($"A recorded {typeof(T).Name} is empty.");

    /// <summary>The plain owner columns, readable without decoding the protected session payload.</summary>
    private sealed record Index(string ActorId, string TenantId, string? Workspace, string? Title, AgentSessionStatus Status,
        DateTimeOffset UpdatedAt, DateTimeOffset? ExpiresAt)
    {
        public static Index From(AgentSession session) => new(session.ActorId, session.TenantId, session.Workspace, session.Title,
            session.Status, session.UpdatedAt, session.ExpiresAt);
    }

    private sealed class Aggregate(string session, Index index)
    {
        public string Session { get; set; } = session;
        public Index Index { get; set; } = index;
        public Ordered Messages { get; } = new();
        public Ordered Context { get; } = new();
        public string? PausedTurn { get; set; }
        public Ordered Proposals { get; } = new();
        public Dictionary<(string Kind, string Key), string> Keys { get; } = new();
        public AgentSessionFence? Owner { get; set; }
        public DateTimeOffset? LeaseExpiresAt { get; set; }
        public long Version { get; set; }
    }

    /// <summary>Serialized values by identity, in first-insertion order.</summary>
    private sealed class Ordered
    {
        private readonly List<string> _order = [];
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AgentActionProposalStatus> _statuses = new(StringComparer.Ordinal);

        public IEnumerable<string> Values => _order.Select(id => _values[id]);

        public int PendingReviewCount => _statuses.Values.Count(x => x is AgentActionProposalStatus.AwaitingApproval or AgentActionProposalStatus.Edited);

        public void Add(string id, string json)
        {
            if (!TryAdd(id, json)) throw new ArgumentException($"The identity '{id}' is recorded twice.", nameof(id));
        }

        public bool TryAdd(string id, string json)
        {
            if (!_values.TryAdd(id, json)) return false;
            _order.Add(id);
            return true;
        }

        public void Upsert(string id, string json, AgentActionProposalStatus? status = null)
        {
            if (!TryAdd(id, json)) _values[id] = json;
            if (status is { } value) _statuses[id] = value;
        }
    }

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

        public static bool Precedes((long Ticks, string Id) cursor, DateTimeOffset updatedAt, string id)
            => updatedAt.UtcTicks < cursor.Ticks || updatedAt.UtcTicks == cursor.Ticks && string.CompareOrdinal(id, cursor.Id) < 0;
    }
}
