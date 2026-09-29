using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Classifies one migration's <c>Up</c> operations as expand-only or not (elsa-workflows/elsa-foundation#2104,
/// spec 185 "Expand-Only Migration Guard"). A migration added after a module's baseline freezes (#1976) may only
/// add: nullable columns, tables and indexes, so hosts still on the previous release keep working against the
/// migrated schema during a rolling upgrade (ADR 0077, ADR 0078's schema version gate).
/// </summary>
/// <remarks>
/// <para>
/// Classification reads <see cref="MigrationOperation"/>s — what EF actually executes — never source text
/// (spec 185 FR-004), against a closed allowed list (FR-006 to FR-011): anything not explicitly allowed is a
/// violation, including an operation type this guard has never seen. A violation's canonical name is
/// <c>&lt;OperationName&gt; &lt;table&gt;[.&lt;column, index or constraint&gt;]</c>, the operation's CLR type name
/// without its <c>Operation</c> suffix and without a schema — a schema belongs to a deployment, not a migration
/// (FR-011).
/// </para>
/// <para>
/// This type and <see cref="ExpandOnlyMigrationOptOutAttribute"/> are public so a third-party EF module can run
/// the same check from its own tests (spec 185, Out of Scope): this guard itself only runs over first-party
/// modules.
/// </para>
/// <para>
/// This is the build-time guard. It also decides which opt-outs are contracting (FR-023): one whose migration
/// removes or renames something on a table a stamped schema family covers must name that family and the version
/// whose finalization makes the removal safe, and no other opt-out may. Refusing to apply a contracting migration
/// before that version is finalized (FR-024, FR-025) is
/// <see cref="SchemaFinalization.EfContractingMigrationCheck"/>'s, at apply time (elsa-workflows/elsa-foundation#2136).
/// </para>
/// </remarks>
public static class ExpandOnlyMigrationGuard
{
    /// <summary>
    /// Every operation in <paramref name="upOperations"/> that is not on the allowed list, in FR-011's canonical
    /// form, in encounter order. A table the same migration creates (a <see cref="CreateTableOperation"/> in the
    /// same list) makes every operation that targets it safe, because no older host's model knows that table yet.
    /// </summary>
    public static IReadOnlyList<string> Classify(IReadOnlyList<MigrationOperation> upOperations)
    {
        ArgumentNullException.ThrowIfNull(upOperations);

        var createdTables = upOperations.OfType<CreateTableOperation>()
            .Select(table => table.Name)
            .ToHashSet(StringComparer.Ordinal);

        return upOperations
            .Where(operation => !IsAllowed(operation, createdTables))
            .Select(CanonicalName)
            .ToArray();
    }

    /// <summary>
    /// Classifies <paramref name="upOperations"/> and matches the result against <paramref name="optOut"/>
    /// (spec 185 FR-013, User Story 3). With no opt-out, the migration passes only when it has no violations.
    /// With one, it passes only when the migration's violations equal the opt-out's list exactly: a violation
    /// the opt-out omits fails, a listed violation that does not occur fails, and an opt-out on a migration with
    /// no violations fails too — an opt-out never becomes a blanket permission.
    /// </summary>
    /// <param name="families">
    /// The tables stamped schema families covered before this migration, and the versions each family reads in this
    /// build: what tells a contracting opt-out from any other (FR-023). There is no overload without it, so a caller
    /// cannot skip that check by omission; <see cref="ExpandOnlyMigrationFamilies.None"/> states that no stamped table
    /// exists before the migration.
    /// </param>
    public static ExpandOnlyMigrationResult Evaluate(
        IReadOnlyList<MigrationOperation> upOperations,
        ExpandOnlyMigrationOptOutAttribute? optOut,
        ExpandOnlyMigrationFamilies families)
    {
        ArgumentNullException.ThrowIfNull(families);
        var violations = Classify(upOperations);
        if (optOut is null)
            return new ExpandOnlyMigrationResult(violations, violations, [], HasOptOut: false);

        var violationSet = violations.ToHashSet(StringComparer.Ordinal);
        var permitted = optOut.Violations.ToHashSet(StringComparer.Ordinal);
        var unlisted = violations.Where(violation => !permitted.Contains(violation)).ToArray();
        var stale = optOut.Violations.Where(entry => !violationSet.Contains(entry)).ToArray();
        return new ExpandOnlyMigrationResult(violations, unlisted, stale, HasOptOut: true)
        {
            ContractionFaults = ContractionFaults(upOperations, optOut, families)
        };
    }

    /// <summary>
    /// FR-023: an opt-out whose migration removes or renames something on a table a stamped schema family covered before
    /// it runs is contracting, and must name exactly that family and a version its chain reads in this build. Any other
    /// opt-out must name neither. One opt-out names one family, so a migration that contracts two families is refused
    /// rather than checked for only one of them at apply time.
    /// </summary>
    private static IReadOnlyList<string> ContractionFaults(
        IReadOnlyList<MigrationOperation> upOperations,
        ExpandOnlyMigrationOptOutAttribute optOut,
        ExpandOnlyMigrationFamilies families)
    {
        var removals = upOperations
            .Select(operation => (Table: RemovedFrom(operation), Operation: operation))
            .Where(removal => removal.Table is not null && families.StampedTables.ContainsKey(removal.Table))
            .Select(removal => (Table: removal.Table!, Family: families.StampedTables[removal.Table!], Violation: CanonicalName(removal.Operation)))
            .ToArray();
        var faults = new List<string>();
        if (removals.Length == 0)
        {
            if (optOut.SchemaFamily is not null || optOut.FinalizedVersion is not null)
                faults.Add($"its opt-out names {Named(optOut)}, but it removes nothing a stamped schema family covers, and only a " +
                           "contracting migration names a family and version (spec 185 FR-023)");
            return faults;
        }

        faults.AddRange(removals
            .Where(removal => removal.Family is null)
            .Select(removal => removal.Table)
            .Distinct(StringComparer.Ordinal)
            .Select(table => $"it removes from stamped table '{table}', and which schema family covers that table cannot be told, " +
                             "so no finalized version can be named for it (spec 185 FR-023)"));
        var contracted = removals
            .Select(removal => removal.Family)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (contracted.Length > 1)
            faults.Add($"it removes from more than one stamped schema family ({string.Join(", ", contracted.Select(family => $"'{family}'"))}), " +
                       "and an opt-out names one family and version: contract each family in a migration of its own (spec 185 FR-023)");
        if (contracted.Length != 1)
            return faults;

        var family = contracted[0];
        var removed = string.Join(", ", removals.Where(removal => removal.Family == family).Select(removal => removal.Violation));
        if (optOut.SchemaFamily is null || optOut.FinalizedVersion is null)
            faults.Add($"it removes what stamped schema family '{family}' covers ({removed}), so its opt-out must name " +
                       $"SchemaFamily = \"{family}\" and the FinalizedVersion whose finalization makes the removal safe (spec 185 FR-023)");
        else if (!StringComparer.Ordinal.Equals(optOut.SchemaFamily, family))
            faults.Add($"its opt-out names schema family '{optOut.SchemaFamily}', but what it removes ({removed}) belongs to '{family}' (spec 185 FR-023)");
        else if (!families.ReadableVersions.TryGetValue(family, out var versions) || !versions.Contains(optOut.FinalizedVersion, StringComparer.Ordinal))
            faults.Add($"its opt-out names version '{optOut.FinalizedVersion}' of '{family}', which this build's chain for that family " +
                       $"does not read ([{string.Join(", ", versions ?? [])}]) (spec 185 FR-023)");
        return faults;
    }

    private static string Named(ExpandOnlyMigrationOptOutAttribute optOut) =>
        (optOut.SchemaFamily, optOut.FinalizedVersion) switch
        {
            ({ } family, { } version) => $"schema family '{family}' and version '{version}'",
            ({ } family, null) => $"schema family '{family}'",
            (null, { } version) => $"version '{version}'",
            _ => "nothing"
        };

    /// <summary>
    /// The table an operation removes or renames something on, or null for any other operation: the operations that
    /// break an older host's read of what they remove, and so the ones a finalized version must be past before they
    /// apply to a table a stamped family covers.
    /// </summary>
    private static string? RemovedFrom(MigrationOperation operation) => operation switch
    {
        DropTableOperation table => table.Name,
        RenameTableOperation table => table.Name,
        DropColumnOperation column => column.Table,
        RenameColumnOperation column => column.Table,
        DropIndexOperation index => index.Table,
        RenameIndexOperation index => index.Table,
        DropForeignKeyOperation foreignKey => foreignKey.Table,
        DropPrimaryKeyOperation primaryKey => primaryKey.Table,
        DropUniqueConstraintOperation unique => unique.Table,
        DropCheckConstraintOperation check => check.Table,
        DeleteDataOperation data => data.Table,
        _ => null
    };

    private static bool IsAllowed(MigrationOperation operation, IReadOnlySet<string> createdTables) => operation switch
    {
        // FR-007: a table the same migration creates — its columns, keys and constraints arrive nested inside
        // CreateTableOperation itself and never appear as separate operations, but an index or a constraint EF
        // emits as its own operation right after still targets a table no older host's model knows about.
        CreateTableOperation => true,
        CreateIndexOperation index => createdTables.Contains(index.Table) || !index.IsUnique,
        AddForeignKeyOperation foreignKey => createdTables.Contains(foreignKey.Table),
        AddPrimaryKeyOperation primaryKey => createdTables.Contains(primaryKey.Table),
        AddUniqueConstraintOperation unique => createdTables.Contains(unique.Table),
        AddCheckConstraintOperation check => createdTables.Contains(check.Table),
        InsertDataOperation insert => createdTables.Contains(insert.Table),
        // FR-008: on a pre-existing table, only a nullable, non-computed column — every older insert and update
        // leaves it unset — and a non-unique index, which changes no result.
        AddColumnOperation column => createdTables.Contains(column.Table) || (column.IsNullable && column.ComputedColumnSql is null),
        // FR-009: neither belongs to any one table.
        EnsureSchemaOperation => true,
        CreateSequenceOperation => true,
        _ => false
    };

    /// <summary>
    /// FR-011's canonical violation name. Every arm below is reachable only for a disallowed operation.
    /// <i>2026-09-28:</i> an operation that carries no table — raw SQL, a database-level alteration, or a kind
    /// this guard has never seen — is named by its operation kind alone (spec 185 Decisions, found while
    /// building #2104).
    /// </summary>
    private static string CanonicalName(MigrationOperation operation)
    {
        var kind = OperationKind(operation);
        return operation switch
        {
            DropTableOperation table => $"{kind} {table.Name}",
            RenameTableOperation table => $"{kind} {table.Name}",
            AlterTableOperation table => $"{kind} {table.Name}",
            // Covers both AddColumnOperation (not nullable, or computed) and AlterColumnOperation (any kind);
            // DropColumnOperation and RenameColumnOperation do not derive from ColumnOperation, so each needs
            // its own arm.
            ColumnOperation column => $"{kind} {column.Table}.{column.Name}",
            DropColumnOperation column => $"{kind} {column.Table}.{column.Name}",
            RenameColumnOperation column => $"{kind} {column.Table}.{column.Name}",
            CreateIndexOperation index => $"{kind} {index.Table}.{index.Name}",
            DropIndexOperation index => $"{kind} {index.Table}.{index.Name}",
            RenameIndexOperation index => $"{kind} {index.Table}.{index.Name}",
            AddForeignKeyOperation foreignKey => $"{kind} {foreignKey.Table}.{foreignKey.Name}",
            DropForeignKeyOperation foreignKey => $"{kind} {foreignKey.Table}.{foreignKey.Name}",
            AddPrimaryKeyOperation primaryKey => $"{kind} {primaryKey.Table}.{primaryKey.Name}",
            DropPrimaryKeyOperation primaryKey => $"{kind} {primaryKey.Table}.{primaryKey.Name}",
            AddUniqueConstraintOperation unique => $"{kind} {unique.Table}.{unique.Name}",
            DropUniqueConstraintOperation unique => $"{kind} {unique.Table}.{unique.Name}",
            AddCheckConstraintOperation check => $"{kind} {check.Table}.{check.Name}",
            DropCheckConstraintOperation check => $"{kind} {check.Table}.{check.Name}",
            InsertDataOperation data => $"{kind} {data.Table}",
            UpdateDataOperation data => $"{kind} {data.Table}",
            DeleteDataOperation data => $"{kind} {data.Table}",
            DropSchemaOperation schema => $"{kind} {schema.Name}",
            CreateSequenceOperation sequence => $"{kind} {sequence.Name}",
            DropSequenceOperation sequence => $"{kind} {sequence.Name}",
            RenameSequenceOperation sequence => $"{kind} {sequence.Name}",
            AlterSequenceOperation sequence => $"{kind} {sequence.Name}",
            RestartSequenceOperation sequence => $"{kind} {sequence.Name}",
            // SqlOperation and AlterDatabaseOperation carry no table or name; a type this guard has never seen
            // may carry neither either, so both fall through to the bare operation kind.
            _ => kind
        };
    }

    private static string OperationKind(MigrationOperation operation) =>
        operation.GetType().Name is var name && name.EndsWith("Operation", StringComparison.Ordinal)
            ? name[..^"Operation".Length]
            : name;
}

/// <summary>
/// One migration's classification against the allowed list and, when it has one, its
/// <see cref="ExpandOnlyMigrationOptOutAttribute"/> (spec 185 FR-013).
/// </summary>
/// <param name="Violations">Every operation that is not on the allowed list, in FR-011's canonical form.</param>
/// <param name="UnlistedViolations">
/// Violations the opt-out does not list — or, with no opt-out, every violation. Non-empty fails the migration.
/// </param>
/// <param name="StaleOptOutEntries">Opt-out entries that name a violation the migration does not contain. Non-empty fails the migration.</param>
/// <param name="HasOptOut">Whether the migration carries an <see cref="ExpandOnlyMigrationOptOutAttribute"/> at all.</param>
public sealed record ExpandOnlyMigrationResult(
    IReadOnlyList<string> Violations,
    IReadOnlyList<string> UnlistedViolations,
    IReadOnlyList<string> StaleOptOutEntries,
    bool HasOptOut)
{
    /// <summary>
    /// Why the opt-out's schema family and version do not fit what the migration removes (FR-023): missing on a
    /// contracting migration, present on any other, naming another family, or naming a version the family does not
    /// read in this build. Non-empty fails the migration.
    /// </summary>
    public IReadOnlyList<string> ContractionFaults { get; init; } = [];

    /// <summary>
    /// With no opt-out: no violations. With one: the migration's violations equal the opt-out's list exactly,
    /// and that list is not empty — an opt-out on a migration with nothing to permit fails too (FR-013) — and
    /// the opt-out names a schema family and version exactly when the migration is contracting (FR-023).
    /// </summary>
    public bool Passed => HasOptOut
        ? Violations.Count > 0 && UnlistedViolations.Count == 0 && StaleOptOutEntries.Count == 0 && ContractionFaults.Count == 0
        : Violations.Count == 0;
}
