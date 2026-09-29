namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

/// <summary>
/// A module context's pending migrations include a contracting migration whose schema family is not yet finalized at
/// the version its opt-out names (spec 185, FR-024 and FR-025), so the whole pending batch is withheld: a context's
/// pending migrations apply as one <c>MigrateAsync</c>, which cannot apply some and hold back others. No operation of
/// any of them has run when this is thrown.
/// </summary>
/// <remarks>
/// <para>
/// It derives from <see cref="EfPendingMigrationsException"/> because the batch is pending, and stays pending until the
/// refusal clears: a caller that reports pending migrations as a negative result, rather than as a database failure,
/// reports this one the same way without a change, and a caller that knows this type names the family and version.
/// </para>
/// <para>
/// Its message is built from the module's name, migration ids, family names and version labels, never from
/// configuration, so it carries no connection string or other restored secret (spec 171, FR-061).
/// </para>
/// </remarks>
public sealed class EfContractingMigrationRefusedException : EfPendingMigrationsException
{
    public EfContractingMigrationRefusedException(string module, IReadOnlyList<string> pending, IReadOnlyList<EfContractingMigrationRefusal> refusals)
        : base(Describe(module, pending, refusals))
    {
        Module = module;
        Pending = pending;
        Refusals = refusals;
    }

    /// <summary>The EF module whose pending batch was withheld.</summary>
    public string Module { get; }

    /// <summary>Every pending migration of the context, none of which was applied.</summary>
    public IReadOnlyList<string> Pending { get; }

    /// <summary>Each pending contracting migration that may not be applied yet, and why.</summary>
    public IReadOnlyList<EfContractingMigrationRefusal> Refusals { get; }

    /// <summary>The refusal's sentence, for a caller that reports it under a subject of its own, such as a feature.</summary>
    public static string Describe(string module, IReadOnlyList<string> pending, IReadOnlyList<EfContractingMigrationRefusal> refusals)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(refusals);
        var remedy = refusals.Any(refusal => refusal.Reason is EfContractingMigrationRefusalReason.NotFinalized or EfContractingMigrationRefusalReason.NoRecord)
            ? "A contracting migration applies only once its family's finalized version in this database reaches the version " +
              "it names (spec 185, FR-024): run every host on a release that reads that version until the finalization gate " +
              "finalizes it, then apply the migrations again."
            : "Correct the migration's [ExpandOnlyMigrationOptOut] so it names a family this module declares and a version " +
              "that family's chain reads (spec 185, FR-023).";
        return $"EF module '{module}' was not migrated: its pending migrations ({string.Join(", ", pending)}) include a " +
               "contracting migration that may not be applied yet, and one context's pending migrations apply as one batch, " +
               $"so none of them was applied. {string.Join(" ", refusals.Select(refusal => refusal.Describe(module) + "."))} {remedy}";
    }
}

/// <summary>One pending contracting migration that may not be applied yet (spec 185, FR-024).</summary>
/// <param name="Migration">The migration's id.</param>
/// <param name="Family">The schema family its opt-out names, if any.</param>
/// <param name="RequiredVersion">The version its opt-out names, if any.</param>
/// <param name="FinalizedVersion">The family's finalized version in the target database; null when it has no record.</param>
/// <param name="Reason">Why it may not be applied.</param>
public sealed record EfContractingMigrationRefusal(
    string Migration,
    string? Family,
    string? RequiredVersion,
    string? FinalizedVersion,
    EfContractingMigrationRefusalReason Reason)
{
    /// <summary>What this refusal says, as a clause naming the migration, the family and both versions.</summary>
    public string Describe(string module) => Reason switch
    {
        EfContractingMigrationRefusalReason.NotFinalized =>
            $"'{Migration}' removes what schema family '{Family}' reads before '{RequiredVersion}', and '{Family}' is finalized " +
            $"at '{FinalizedVersion}' in this database, not at '{RequiredVersion}' or later",
        EfContractingMigrationRefusalReason.NoRecord =>
            $"'{Migration}' removes what schema family '{Family}' reads before '{RequiredVersion}', and '{Family}' has no " +
            $"finalization record in this database although EF module '{module}' has been admitted there, so nothing shows " +
            $"'{RequiredVersion}' finalized",
        EfContractingMigrationRefusalReason.UnknownFamily =>
            $"'{Migration}' names schema family '{Family}' in its opt-out, which EF module '{module}' does not declare, so no " +
            "finalized version can show its removal safe",
        EfContractingMigrationRefusalReason.UnknownVersion =>
            $"'{Migration}' names version '{RequiredVersion}' of schema family '{Family}' in its opt-out, which this build's " +
            "chain for that family does not read, so no finalized version can show its removal safe",
        EfContractingMigrationRefusalReason.Incomplete =>
            $"'{Migration}' names {(Family is null ? $"version '{RequiredVersion}' but no schema family" : $"schema family '{Family}' but no version")} " +
            "in its opt-out, so no finalized version can show its removal safe",
        _ => throw new ArgumentOutOfRangeException(nameof(Reason), Reason, "Unknown contracting migration refusal.")
    };
}

/// <summary>Why a pending contracting migration may not be applied yet.</summary>
public enum EfContractingMigrationRefusalReason
{
    /// <summary>The family's finalized version is not the version the opt-out names or one after it on this build's chain.</summary>
    NotFinalized,

    /// <summary>The module has been admitted in the database, yet the family has no finalization record there.</summary>
    NoRecord,

    /// <summary>The opt-out names a family the migration's module does not declare.</summary>
    UnknownFamily,

    /// <summary>The opt-out names a version the family's chain does not read in this build.</summary>
    UnknownVersion,

    /// <summary>The opt-out names a family without a version, or a version without a family.</summary>
    Incomplete
}
