namespace Elsa.Weaver.Persistence.EntityFrameworkCore.Entities;

/// <summary>
/// One session aggregate's root: the plain owner, list and ownership columns, and the protected session and paused
/// turn. Every write to the aggregate changes <see cref="Version"/> here first, so it serializes the aggregate's writers.
/// </summary>
internal sealed class WeaverSessionRow
{
    public string SessionId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string? Workspace { get; set; }
    public string? Title { get; set; }
    public int Status { get; set; }
    public long UpdatedAtUtcTicks { get; set; }
    public long? ExpiresAtUtcTicks { get; set; }
    public long? PolicyRevision { get; set; }
    public string? ContinuedFrom { get; set; }
    /// <summary>The documented session limit the session reached, so the owner list reports it without decoding.</summary>
    public string? LimitReached { get; set; }
    public string OwnerToken { get; set; } = "";
    public long Fence { get; set; }
    public long? LeaseExpiresAtUtcTicks { get; set; }
    public long Version { get; set; }
    /// <summary>The next insertion position of the aggregate's messages, snapshots, context and proposals.</summary>
    public long NextSequence { get; set; }
    public string SchemaVersion { get; set; } = "";
    /// <summary>Protected <c>WeaverSession</c>.</summary>
    public string Payload { get; set; } = "";
    /// <summary>Protected <c>WeaverPausedTurnRecord</c>, or null when no turn is paused.</summary>
    public string? PausedTurnPayload { get; set; }
    /// <summary>Protected audit records a recorded recovery settlement still owes, or null when it owes none.</summary>
    public string? PendingAuditsPayload { get; set; }
}

internal sealed class WeaverMessageRow
{
    public string SessionId { get; set; } = "";
    public string MessageId { get; set; } = "";
    public long Sequence { get; set; }
    public int Role { get; set; }
    public int Status { get; set; }
    public string? StatusReason { get; set; }
    public string SchemaVersion { get; set; } = "";
    /// <summary>Protected <c>WeaverMessageRecord</c>.</summary>
    public string Payload { get; set; } = "";
}

internal sealed class WeaverDisclosureSnapshotRow
{
    public string SessionId { get; set; } = "";
    public string Digest { get; set; } = "";
    public long Sequence { get; set; }
    public long Size { get; set; }
    public string SchemaVersion { get; set; } = "";
    /// <summary>Protected canonical JSON text, byte-identical to what was digested.</summary>
    public string Canonical { get; set; } = "";
}

internal sealed class WeaverContextRow
{
    public string SessionId { get; set; } = "";
    /// <summary>SHA-256 of the attachment identity, which callers choose and which may exceed a key column.</summary>
    public string AttachmentKey { get; set; } = "";
    public long Sequence { get; set; }
    public string SchemaVersion { get; set; } = "";
    /// <summary>Protected <c>WeaverContextAttachment</c>.</summary>
    public string Payload { get; set; } = "";
}

internal sealed class WeaverProposalRow
{
    public string ProposalId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public long Sequence { get; set; }
    public int Status { get; set; }
    /// <summary>The prepared deadline, so the owner list counts only reviews that can still be decided without decoding.</summary>
    public long? ExpiresAtUtcTicks { get; set; }
    public long Version { get; set; }
    public string SchemaVersion { get; set; } = "";
    /// <summary>Protected <c>WeaverProposalRecord</c>; its <c>Version</c> is the column.</summary>
    public string Payload { get; set; } = "";
}

internal sealed class WeaverCallKeyRow
{
    public string SessionId { get; set; } = "";
    public string Kind { get; set; } = "";
    /// <summary>SHA-256 of <see cref="Key"/>; keys are composed of caller identities of unbounded length.</summary>
    public string KeyHash { get; set; } = "";
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public long Sequence { get; set; }
    public string SchemaVersion { get; set; } = "";
}

internal sealed class WeaverAuditRow
{
    /// <summary>The append order, assigned as the next position under the primary key; it breaks ties between records created at the same time.</summary>
    public long Sequence { get; set; }
    public string EventId { get; set; } = "";
    public int Kind { get; set; }
    public string? SessionId { get; set; }
    public string? ActorId { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public string SchemaVersion { get; set; } = "";
    /// <summary>Protected <c>WeaverAuditEvent</c>.</summary>
    public string Payload { get; set; } = "";
}

/// <summary>
/// The single counter row that allocates audit append positions. An append bumps it with a locking update inside its
/// transaction, so concurrent appends queue on it instead of colliding on the next position, and a rolled-back append
/// leaves no gap.
/// </summary>
internal sealed class WeaverAuditSequenceRow
{
    public int Id { get; set; }
    /// <summary>The last append position allocated.</summary>
    public long Last { get; set; }
    public string SchemaVersion { get; set; } = "";
}

/// <summary>The first-wins owner association that scopes audit queries; it survives restarts with the records.</summary>
internal sealed class WeaverAuditOwnerRow
{
    public string SessionId { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string SchemaVersion { get; set; } = "";
}
