namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

/// <summary>
/// One schema family's finalization status in one database (spec 181, FR-022): the finalized version, each version
/// this host reads above it with why it is pending, any intent in flight and the holds, and what this host writes.
/// Spec 182 carries it to the feature catalog and Attention; the CLI's <c>status</c> prints it.
/// </summary>
/// <param name="WriteVersion">The version this host writes, or null where no host is asking (the CLI).</param>
/// <param name="WritesRefused">True when this host refuses every write to the family (FR-012).</param>
/// <param name="ObservedAt">
/// When this host last read the family's record, or null before it has, or where no host is asking (the CLI).
/// </param>
public sealed record EfSchemaFamilyStatus(
    string Family,
    string? Module,
    string? DatabaseIdentity,
    string? FinalizedVersion,
    IReadOnlyList<string> ReadableVersions,
    SchemaFinalizationIntent? Intent,
    IReadOnlyList<SchemaFinalizationHold> Holds,
    IReadOnlyList<EfSchemaPendingVersion> Pending,
    SchemaFinishRecord? Finish,
    string? WriteVersion = null,
    bool WritesRefused = false,
    DateTimeOffset? ObservedAt = null)
{
    /// <summary>
    /// Describes <paramref name="record"/> against <paramref name="readable"/>, the versions this build reads of
    /// <paramref name="family"/>, oldest first. Each version this build reads after the finalized one is listed as pending
    /// or readable everywhere, with the holds that keep it and, when <paramref name="blockers"/> is given, the counted
    /// members that cannot read it.
    /// </summary>
    public static EfSchemaFamilyStatus Describe(
        string family,
        string? module,
        IReadOnlyList<string> readable,
        SchemaFinalizationRecord? record,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? blockers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentNullException.ThrowIfNull(readable);
        if (record is null)
            return new EfSchemaFamilyStatus(family, module, null, null, readable, null, [], [], null);

        var finalizedAt = SchemaVersionChain.PositionOf(readable, record.FinalizedVersion);
        var pending = finalizedAt < 0
            ? []
            : readable
                .Skip(finalizedAt + 1)
                .Select(version => new EfSchemaPendingVersion(
                    version,
                    record.StateOf(version, readable),
                    record.Holds.Where(hold => hold.AppliesTo(version, readable)).ToArray(),
                    blockers is not null && blockers.TryGetValue(version, out var members) ? members : null))
                .ToArray();
        return new EfSchemaFamilyStatus(
            family,
            module,
            record.DatabaseIdentity,
            record.FinalizedVersion,
            readable,
            record.Intent,
            record.Holds,
            pending,
            record.Finish);
    }
}

/// <summary>
/// A version above the finalized one and why it is not finalized: the holds that apply to it, and the counted members
/// that cannot read it, or null when the fleet was not asked.
/// </summary>
public sealed record EfSchemaPendingVersion(
    string Version,
    SchemaFinalizationState State,
    IReadOnlyList<SchemaFinalizationHold> HeldBy,
    IReadOnlyList<string>? Blockers);
