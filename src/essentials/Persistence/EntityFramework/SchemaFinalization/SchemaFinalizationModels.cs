using System.Text.Json.Serialization;

namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

/// <summary>
/// One schema family's finalization record in one database (spec 181, FR-001): the opaque database identity, the
/// finalized version, at most one intent in flight, the active holds and the append-only history of transitions, and
/// the finish record spec 186 keeps inside it with its own append-only history.
/// </summary>
/// <param name="Revision">
/// Raised by one on every change. Every write names the revision it read, and is refused as a conflict if the record
/// has changed since, so every change is a compare-and-set against the whole record.
/// </param>
public sealed record SchemaFinalizationRecord(
    string Family,
    string DatabaseIdentity,
    long Revision,
    string FinalizedVersion,
    SchemaFinalizationIntent? Intent,
    IReadOnlyList<SchemaFinalizationHold> Holds,
    IReadOnlyList<SchemaFinalizationHistoryEntry> History,
    SchemaFinishRecord? Finish,
    IReadOnlyList<SchemaFinishHistoryEntry> FinishHistory)
{
    /// <summary>
    /// The state of <paramref name="version"/> (spec 181, FR-004): finalized when it is at or before the finalized
    /// version along <paramref name="chain"/>; readable everywhere when an intent to finalize it, or a later version, is
    /// in flight and no hold applies to it; pending otherwise.
    /// </summary>
    /// <param name="chain">The family's versions, oldest first, as this build declares them.</param>
    /// <exception cref="SchemaFinalizationRefusedException">
    /// <paramref name="version"/> or the finalized version is not in <paramref name="chain"/>, so this build cannot
    /// place one relative to the other.
    /// </exception>
    public SchemaFinalizationState StateOf(string version, IReadOnlyList<string> chain)
    {
        var position = SchemaVersionChain.Require(Family, chain, version, "asked about");
        if (position <= SchemaVersionChain.Require(Family, chain, FinalizedVersion, "finalized"))
            return SchemaFinalizationState.Finalized;
        if (IsHeld(version, chain))
            return SchemaFinalizationState.Pending;
        return Intent is not null && SchemaVersionChain.PositionOf(chain, Intent.Version) >= position
            ? SchemaFinalizationState.ReadableEverywhere
            : SchemaFinalizationState.Pending;
    }

    /// <summary>Whether any hold keeps <paramref name="version"/> from finalizing.</summary>
    public bool IsHeld(string version, IReadOnlyList<string> chain) => Holds.Any(hold => hold.AppliesTo(version, chain));
}

/// <summary>The state of one version above a family's finalized version (spec 181, FR-004). A hold keeps a version pending.</summary>
public enum SchemaFinalizationState
{
    /// <summary>Some counted member cannot read it, or a hold applies.</summary>
    Pending,

    /// <summary>An intent to finalize it is durable and being confirmed.</summary>
    ReadableEverywhere,

    /// <summary>Every host may write it. Terminal: no transition leaves it.</summary>
    Finalized
}

/// <summary>One host run: a host id and its incarnation, as the cluster membership contract names a member.</summary>
public sealed record SchemaFinalizationMember
{
    public SchemaFinalizationMember(string hostId, string incarnation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        ArgumentException.ThrowIfNullOrWhiteSpace(incarnation);
        HostId = hostId;
        Incarnation = incarnation;
    }

    public string HostId { get; }

    public string Incarnation { get; }

    public override string ToString() => $"{HostId} ({Incarnation})";
}

/// <summary>Who is responsible for a transition: a member, or an operator by the identity they gave.</summary>
public sealed record SchemaFinalizationActor
{
    public SchemaFinalizationActor(SchemaFinalizationMember? member, string? @operator)
    {
        if ((member is null) == (@operator is null))
            throw new ArgumentException("A transition is made either by a member or by an operator, never both or neither.");
        if (@operator is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(@operator);
        Member = member;
        Operator = @operator;
    }

    public SchemaFinalizationMember? Member { get; }

    public string? Operator { get; }

    public static SchemaFinalizationActor Of(SchemaFinalizationMember member) =>
        new(member ?? throw new ArgumentNullException(nameof(member)), null);

    public static SchemaFinalizationActor OfOperator(string identity) => new(null, identity);

    public override string ToString() => Member?.ToString() ?? $"operator {Operator}";
}

/// <summary>A durable "about to finalize <paramref name="Version"/>", written before the confirming membership read (spec 181, FR-006).</summary>
public sealed record SchemaFinalizationIntent(string Version, SchemaFinalizationMember Member, DateTimeOffset At);

/// <summary>An operator's durable instruction that a family must not finalize, or, with a version, must not finalize it (spec 181, FR-019).</summary>
public sealed record SchemaFinalizationHold(string? Version, string Reason, string PlacedBy, DateTimeOffset PlacedAt)
{
    /// <summary>
    /// Whether this hold keeps <paramref name="target"/> from finalizing. A family-wide hold applies to every version. A
    /// hold on one version also applies to every later one, because finalizing a later version moves the finalized
    /// version, and with it the rollback boundary, past the held one. A hold on a version <paramref name="chain"/> does
    /// not name applies to everything, since this build cannot tell whether finalizing would pass it.
    /// </summary>
    public bool AppliesTo(string target, IReadOnlyList<string> chain)
    {
        if (Version is null)
            return true;
        var held = SchemaVersionChain.PositionOf(chain, Version);
        return held < 0 || held <= SchemaVersionChain.PositionOf(chain, target);
    }
}

/// <summary>What a history entry records.</summary>
public enum SchemaFinalizationTransition
{
    Created,
    IntentRecorded,
    IntentAbandoned,
    Finalized,
    HoldPlaced,
    HoldReleased
}

/// <summary>One append-only entry in a record's history. <paramref name="Version"/> is null only for a family-wide hold.</summary>
public sealed record SchemaFinalizationHistoryEntry(
    SchemaFinalizationTransition Transition,
    string? Version,
    SchemaFinalizationActor Actor,
    DateTimeOffset At,
    string? Reason = null);

/// <summary>
/// Spec 186's completeness proof, inside the finalization record: no row of the family below
/// <paramref name="CompletionVersion"/> remains in the database. The verification instants are null for the completion
/// recorded when the record was created, which no verification pass proved because no older row can exist (spec 186,
/// FR-016).
/// </summary>
/// <param name="Run">
/// The claim of the backfill run upgrading the family past <paramref name="CompletionVersion"/>, if one holds it (spec
/// 186, FR-008). It only spares duplicate work: nothing correct depends on it, so it is absent from the JSON of a record
/// no run has claimed, a build that does not know it reads the record unchanged, and a completion or a withdrawal drops
/// it.
/// </param>
public sealed record SchemaFinishRecord(
    string CompletionVersion,
    DateTimeOffset? VerificationStartedAt,
    DateTimeOffset? VerificationEndedAt,
    SchemaFinalizationActor RecordedBy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    SchemaBackfillClaim? Run = null);

/// <summary>
/// A backfill worker's claim on a family's run in one database (spec 186, FR-008): the member and the worker that holds
/// it, the version the run upgrades to, and until when it holds it. Another worker leaves the run alone until the claim
/// expires, so a crashed worker delays the run by at most one claim period and never stops it.
/// </summary>
/// <param name="Worker">
/// The worker within the member: one host serving the same database from two shells runs two workers under one member.
/// </param>
public sealed record SchemaBackfillClaim(
    SchemaFinalizationMember Member,
    string Worker,
    string TargetVersion,
    DateTimeOffset ClaimedAt,
    DateTimeOffset ExpiresAt)
{
    /// <summary>Whether the claim still holds at <paramref name="now"/>.</summary>
    public bool HoldsAt(DateTimeOffset now) => ExpiresAt > now;
}

/// <summary>What a finish history entry records.</summary>
public enum SchemaFinishTransition
{
    Completed,
    Withdrawn
}

/// <summary>One append-only entry in a record's finish history (spec 186, FR-014 and FR-018).</summary>
public sealed record SchemaFinishHistoryEntry(
    SchemaFinishTransition Transition,
    string Version,
    SchemaFinalizationActor Actor,
    DateTimeOffset At,
    string? Reason = null);

/// <summary>
/// The outcome of a compare-and-set write. <see cref="Applied"/> is false when the record had changed since the
/// revision the writer named; nothing was written, and <see cref="Record"/> is the record as it stands, to decide again from.
/// </summary>
public sealed record SchemaFinalizationWrite(bool Applied, SchemaFinalizationRecord Record);

/// <summary>Positions of opaque version labels along a family's chain, which alone orders them (spec 180, FR-004).</summary>
internal static class SchemaVersionChain
{
    public static int PositionOf(IReadOnlyList<string> chain, string version)
    {
        for (var position = 0; position < chain.Count; position++)
        {
            if (StringComparer.Ordinal.Equals(chain[position], version))
                return position;
        }

        return -1;
    }

    /// <summary>The position of <paramref name="version"/>, refusing a version <paramref name="chain"/> does not name.</summary>
    public static int Require(string family, IReadOnlyList<string> chain, string version, string role)
    {
        Validate(chain);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var position = PositionOf(chain, version);
        return position >= 0
            ? position
            : throw new SchemaFinalizationRefusedException(
                family,
                SchemaFinalizationRefusal.UnknownVersion,
                $"the {role} version '{version}' is not in this build's chain [{string.Join(", ", chain)}], so it cannot be placed relative to the others.");
    }

    public static void Validate(IReadOnlyList<string> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Count == 0 || chain.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A family's chain names at least its current version, and no blank one.", nameof(chain));
        if (chain.Distinct(StringComparer.Ordinal).Count() != chain.Count)
            throw new ArgumentException($"A family's chain names each version once: [{string.Join(", ", chain)}].", nameof(chain));
        if (chain.Any(version => version.Length > EfSchemaFinalization.MaxVersionLength))
            throw new ArgumentException($"A version label is at most {EfSchemaFinalization.MaxVersionLength} characters, the width of a stamp column.", nameof(chain));
    }
}
