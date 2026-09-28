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
/// This is the build-time guard only. Spec 185's apply-time refusal of an early contracting migration
/// (FR-023 to FR-025) is tracked separately in elsa-workflows/elsa-foundation#2136 and is not implemented here.
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
    public static ExpandOnlyMigrationResult Evaluate(IReadOnlyList<MigrationOperation> upOperations, ExpandOnlyMigrationOptOutAttribute? optOut)
    {
        var violations = Classify(upOperations);
        if (optOut is null)
            return new ExpandOnlyMigrationResult(violations, violations, [], HasOptOut: false);

        var violationSet = violations.ToHashSet(StringComparer.Ordinal);
        var permitted = optOut.Violations.ToHashSet(StringComparer.Ordinal);
        var unlisted = violations.Where(violation => !permitted.Contains(violation)).ToArray();
        var stale = optOut.Violations.Where(entry => !violationSet.Contains(entry)).ToArray();
        return new ExpandOnlyMigrationResult(violations, unlisted, stale, HasOptOut: true);
    }

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
    /// With no opt-out: no violations. With one: the migration's violations equal the opt-out's list exactly,
    /// and that list is not empty — an opt-out on a migration with nothing to permit fails too (FR-013).
    /// </summary>
    public bool Passed => HasOptOut
        ? Violations.Count > 0 && UnlistedViolations.Count == 0 && StaleOptOutEntries.Count == 0
        : Violations.Count == 0;
}
