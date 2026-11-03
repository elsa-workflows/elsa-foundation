using System.Text;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Elsa.Agent.Persistence.EntityFrameworkCore.Services;

namespace Elsa.Agent.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfAgentSessionRecordStore
{
    private const string SessionRowId = "session";
    private const string PausedTurnRowId = "paused-turn";
    private const string PendingAuditsRowId = "pending-audits";

    /// <summary>
    /// Protects every payload of one create or write and derives its plain columns. Identities are grouped as the default
    /// store applies them: messages and proposals upsert (first position, last value), snapshots, context and keys keep the
    /// first value of an identity.
    /// </summary>
    private Encoded Encode(string sessionId, AgentSessionRecordWrite write, bool refuseDuplicateContext)
    {
        Bounded(sessionId, AgentModelBuilderExtensions.IdentityLength, "session identity");
        AgentSessionRow? session = null;
        if (write.Session is { } value)
        {
            Bounded(value.TenantId, AgentModelBuilderExtensions.PrincipalLength, "tenant");
            Bounded(value.ActorId, AgentModelBuilderExtensions.PrincipalLength, "actor");
            if (value.Workspace is not null) Bounded(value.Workspace, AgentModelBuilderExtensions.PrincipalLength, "workspace");
            if (value.ContinuedFrom is not null) Bounded(value.ContinuedFrom, AgentModelBuilderExtensions.IdentityLength, "continued session identity");
            if (value.LimitReached is not null) Bounded(value.LimitReached, AgentModelBuilderExtensions.KindLength, "session limit");
            session = new AgentSessionRow
            {
                SessionId = sessionId, TenantId = value.TenantId, ActorId = value.ActorId, Workspace = value.Workspace, Title = value.Title,
                Status = (int)value.Status, UpdatedAtUtcTicks = value.UpdatedAt.UtcTicks, ExpiresAtUtcTicks = value.ExpiresAt?.UtcTicks,
                PolicyRevision = value.PolicyRevision, ContinuedFrom = value.ContinuedFrom, LimitReached = value.LimitReached, SchemaVersion = AgentEfModule.SchemaVersion,
                Payload = codec.Protect(AgentRecordCodec.SessionPurpose, sessionId, SessionRowId, value)
            };
        }

        var messages = write.Messages
            .Select(record => (Record: record, Id: Bounded(record.Message.Id, AgentModelBuilderExtensions.IdentityLength, "message identity")))
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .Select(group => group.Last().Record)
            .Select(record => new EncodedMessage(record.Message.Id, (int)record.Message.Role, (int)record.Message.Status, record.Message.StatusReason,
                codec.Protect(AgentRecordCodec.MessagePurpose, sessionId, record.Message.Id, record)))
            .ToArray();
        var snapshots = write.Snapshots.DistinctBy(x => x.Digest, StringComparer.Ordinal)
            .Select(snapshot => new EncodedSnapshot(Bounded(snapshot.Digest, AgentModelBuilderExtensions.HashLength, "snapshot digest"), snapshot.Size,
                codec.ProtectText(AgentRecordCodec.SnapshotPurpose, sessionId, snapshot.Digest, snapshot.Canonical)))
            .ToArray();
        if (refuseDuplicateContext && write.Context.GroupBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1) is { } duplicate)
            throw new ArgumentException($"The identity '{duplicate.Key}' is recorded twice.", nameof(write));
        var context = write.Context.DistinctBy(x => x.Id, StringComparer.Ordinal)
            .Select(attachment =>
            {
                var key = AgentRecordCodec.Hash(attachment.Id);
                return new EncodedContext(key, codec.Protect(AgentRecordCodec.ContextPurpose, sessionId, key, attachment));
            })
            .ToArray();
        var proposals = write.Proposals
            .GroupBy(x => Bounded(x.Proposal.Id, AgentModelBuilderExtensions.IdentityLength, "proposal identity"), StringComparer.Ordinal)
            .Select(group => new EncodedProposal(group.Key, (int)group.Last().Proposal.Status, group.Last().Proposal.PreparedInvocation?.ExpiresAt.UtcTicks,
                group.Select(x => x.Version).ToArray(),
                codec.Protect(AgentRecordCodec.ProposalPurpose, sessionId, group.Key, group.Last())))
            .ToArray();
        var keys = write.Keys.DistinctBy(x => (x.Kind, x.Key))
            .Select(key => new EncodedKey(Bounded(key.Kind, AgentModelBuilderExtensions.KindLength, "key kind"), key.Key, AgentRecordCodec.Hash(key.Key), key.Value))
            .ToArray();
        var paused = write.PausedTurn is null ? null : codec.Protect(AgentRecordCodec.PausedTurnPurpose, sessionId, PausedTurnRowId, write.PausedTurn);
        var pendingAudits = write.PendingAudits is not { Count: > 0 } owed ? null
            : codec.Protect(AgentRecordCodec.PendingAuditsPurpose, sessionId, PendingAuditsRowId, owed.ToArray());
        return new(session, messages, snapshots, context, proposals, keys, paused) { WritesPendingAudits = write.PendingAudits is not null, PendingAudits = pendingAudits };
    }

    /// <summary>Authenticates and decodes one consistent read, checking every plain column against its protected record.</summary>
    private AgentSessionRecord Decode(string sessionId, LoadedRows rows)
    {
        var row = rows.Session;
        Current(row.SchemaVersion);
        var session = codec.Unprotect<AgentSession>(AgentRecordCodec.SessionPurpose, sessionId, SessionRowId, row.Payload);
        if (session.Id != sessionId || session.TenantId != row.TenantId || session.ActorId != row.ActorId || session.Workspace != row.Workspace
            || session.Title != row.Title || (int)session.Status != row.Status || session.UpdatedAt.UtcTicks != row.UpdatedAtUtcTicks
            || session.ExpiresAt?.UtcTicks != row.ExpiresAtUtcTicks || session.PolicyRevision != row.PolicyRevision || session.ContinuedFrom != row.ContinuedFrom
            || session.LimitReached != row.LimitReached)
            throw new InvalidDataException("The agent session columns do not match the protected session record.");

        var messages = rows.Messages.Select(message =>
        {
            Current(message.SchemaVersion);
            var record = codec.Unprotect<AgentMessageRecord>(AgentRecordCodec.MessagePurpose, sessionId, message.MessageId, message.Payload);
            if (record.Message.Id != message.MessageId || (int)record.Message.Role != message.Role || (int)record.Message.Status != message.Status
                || record.Message.StatusReason != message.StatusReason)
                throw new InvalidDataException("An agent message's columns do not match its protected record.");
            return record;
        }).ToArray();
        var snapshots = rows.Snapshots.Select(snapshot =>
        {
            Current(snapshot.SchemaVersion);
            var record = new AgentDisclosureSnapshotRecord(snapshot.Digest,
                codec.UnprotectText(AgentRecordCodec.SnapshotPurpose, sessionId, snapshot.Digest, snapshot.Canonical));
            if (record.Size != snapshot.Size)
                throw new InvalidDataException("A disclosure snapshot's recorded size does not match its protected text.");
            return record;
        }).ToArray();
        var context = rows.Context.Select(attachment =>
        {
            Current(attachment.SchemaVersion);
            var value = codec.Unprotect<AgentContextAttachment>(AgentRecordCodec.ContextPurpose, sessionId, attachment.AttachmentKey, attachment.Payload);
            if (AgentRecordCodec.Hash(value.Id) != attachment.AttachmentKey)
                throw new InvalidDataException("A context attachment's key does not match its protected record.");
            return value;
        }).ToArray();
        var proposals = rows.Proposals.Select(proposal =>
        {
            Current(proposal.SchemaVersion);
            var record = codec.Unprotect<AgentProposalRecord>(AgentRecordCodec.ProposalPurpose, sessionId, proposal.ProposalId, proposal.Payload);
            if (record.Proposal.Id != proposal.ProposalId || (int)record.Proposal.Status != proposal.Status || proposal.Version < 1
                || record.Proposal.PreparedInvocation?.ExpiresAt.UtcTicks != proposal.ExpiresAtUtcTicks)
                throw new InvalidDataException("A proposal's columns do not match its protected record.");
            return record with { Version = proposal.Version };
        }).ToArray();
        var keys = rows.Keys.Select(key =>
        {
            Current(key.SchemaVersion);
            if (AgentRecordCodec.Hash(key.Key) != key.KeyHash)
                throw new InvalidDataException("A duplicate-detection key does not match its recorded hash.");
            return new AgentSessionUniqueKey(key.Kind, key.Key, key.Value);
        }).ToArray();
        var paused = row.PausedTurnPayload is null ? null
            : codec.Unprotect<AgentPausedTurnRecord>(AgentRecordCodec.PausedTurnPurpose, sessionId, PausedTurnRowId, row.PausedTurnPayload);
        IReadOnlyList<AgentAuditEvent> pendingAudits = row.PendingAuditsPayload is null ? []
            : codec.Unprotect<AgentAuditEvent[]>(AgentRecordCodec.PendingAuditsPurpose, sessionId, PendingAuditsRowId, row.PendingAuditsPayload);

        return new AgentSessionRecord(session)
        {
            Messages = messages, Snapshots = snapshots, Context = context, PausedTurn = paused, Proposals = proposals, Keys = keys, PendingAudits = pendingAudits,
            Owner = new(row.OwnerToken, row.Fence),
            LeaseExpiresAt = Time(row.LeaseExpiresAtUtcTicks),
            Version = row.Version
        };
    }

    private static void Current(string schemaVersion)
    {
        if (schemaVersion != AgentEfModule.SchemaVersion)
            throw new InvalidDataException($"The agent record schema '{schemaVersion}' is not supported by this build.");
    }

    private static string Bounded(string value, int length, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length > length)
            throw new ArgumentException($"The {name} exceeds the durable agent store's limit of {length} characters.", name);
        return value;
    }

    private sealed record LoadedRows(
        AgentSessionRow Session,
        IReadOnlyList<AgentMessageRow> Messages,
        IReadOnlyList<AgentDisclosureSnapshotRow> Snapshots,
        IReadOnlyList<AgentContextRow> Context,
        IReadOnlyList<AgentProposalRow> Proposals,
        IReadOnlyList<AgentCallKeyRow> Keys);

    private sealed record Encoded(
        AgentSessionRow? Session,
        IReadOnlyList<EncodedMessage> Messages,
        IReadOnlyList<EncodedSnapshot> Snapshots,
        IReadOnlyList<EncodedContext> Context,
        IReadOnlyList<EncodedProposal> Proposals,
        IReadOnlyList<EncodedKey> Keys,
        string? PausedTurn)
    {
        /// <summary>Whether the write sets the owed audit records (to <see cref="PendingAudits"/>, null clearing them).</summary>
        public bool WritesPendingAudits { get; init; }
        public string? PendingAudits { get; init; }
    }

    private sealed record EncodedMessage(string Id, int Role, int Status, string? StatusReason, string Payload)
    {
        public AgentMessageRow Row(string sessionId, long sequence) => new()
        {
            SessionId = sessionId, MessageId = Id, Sequence = sequence, Role = Role, Status = Status, StatusReason = StatusReason,
            SchemaVersion = AgentEfModule.SchemaVersion, Payload = Payload
        };

        public void Apply(AgentMessageRow row)
        {
            row.Role = Role;
            row.Status = Status;
            row.StatusReason = StatusReason;
            row.SchemaVersion = AgentEfModule.SchemaVersion;
            row.Payload = Payload;
        }
    }

    private sealed record EncodedSnapshot(string Digest, long Size, string Canonical)
    {
        public AgentDisclosureSnapshotRow Row(string sessionId, long sequence) => new()
        {
            SessionId = sessionId, Digest = Digest, Sequence = sequence, Size = Size, SchemaVersion = AgentEfModule.SchemaVersion, Canonical = Canonical
        };
    }

    private sealed record EncodedContext(string Key, string Payload)
    {
        public AgentContextRow Row(string sessionId, long sequence) => new()
        {
            SessionId = sessionId, AttachmentKey = Key, Sequence = sequence, SchemaVersion = AgentEfModule.SchemaVersion, Payload = Payload
        };
    }

    /// <param name="ReadVersions">The version each occurrence in the write was decided on; every one must be current.</param>
    private sealed record EncodedProposal(string Id, int Status, long? ExpiresAtUtcTicks, IReadOnlyList<long> ReadVersions, string Payload)
    {
        public int Occurrences => ReadVersions.Count;

        public AgentProposalRow Row(string sessionId, long sequence, int version) => new()
        {
            ProposalId = Id, SessionId = sessionId, Sequence = sequence, Status = Status, ExpiresAtUtcTicks = ExpiresAtUtcTicks, Version = version,
            SchemaVersion = AgentEfModule.SchemaVersion, Payload = Payload
        };

        public void Apply(AgentProposalRow row)
        {
            row.Status = Status;
            row.ExpiresAtUtcTicks = ExpiresAtUtcTicks;
            row.Version += Occurrences;
            row.SchemaVersion = AgentEfModule.SchemaVersion;
            row.Payload = Payload;
        }
    }

    private sealed record EncodedKey(string Kind, string Key, string Hash, string Value)
    {
        public AgentCallKeyRow Row(string sessionId, long sequence) => new()
        {
            SessionId = sessionId, Kind = Kind, KeyHash = Hash, Key = Key, Value = Value, Sequence = sequence, SchemaVersion = AgentEfModule.SchemaVersion
        };
    }
}
