using System.Reflection;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// What <see cref="ExpandOnlyMigrationGuard.Evaluate"/> needs to tell a contracting migration from any other (spec 185
/// FR-023): which tables a stamped schema family covered before the migration runs, and the versions each of the
/// module's families reads in this build.
/// </summary>
/// <param name="StampedTables">
/// Every table whose rows carried a schema-version stamp before the migration, without its schema, mapped to the family
/// those rows belong to, or to <see langword="null"/> when which family cannot be told. A table missing here is not
/// stamped, so removing something from it is not contracting.
/// </param>
/// <param name="ReadableVersions">Each of the module's families, mapped to the versions its chain reads in this build.</param>
public sealed record ExpandOnlyMigrationFamilies(
    IReadOnlyDictionary<string, string?> StampedTables,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ReadableVersions)
{
    /// <summary>No table was stamped before the migration, so no opt-out on it is contracting.</summary>
    public static ExpandOnlyMigrationFamilies None { get; } = new(
        new Dictionary<string, string?>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

    /// <summary>
    /// The stamped tables of <paramref name="modelBefore"/>, the target model of the migration just before the one under
    /// inspection (<see langword="null"/> for a context's first migration, which nothing precedes), with the families of
    /// <paramref name="families"/>. The finalization record's own tables are never a module family's.
    /// </summary>
    /// <param name="moduleAssembly">
    /// Where a snapshot's entity type is resolved by name when the module owns more than one family: a migration's
    /// target model names its entity types rather than holding them.
    /// </param>
    public static ExpandOnlyMigrationFamilies Before(IReadOnlyModel? modelBefore, EfSchemaModuleFamilies families, Assembly moduleAssembly)
    {
        ArgumentNullException.ThrowIfNull(families);
        ArgumentNullException.ThrowIfNull(moduleAssembly);
        var stamped = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var entityType in modelBefore?.GetEntityTypes() ?? [])
        {
            if (entityType.IsOwned() ||
                entityType.FindProperty(EfSchemaVersion.ColumnName) is null ||
                entityType.GetTableName() is not { } table ||
                table.StartsWith(EfSchemaFinalization.RecordTablePrefix, StringComparison.Ordinal) ||
                table.StartsWith(EfSchemaFinalization.DatabaseIdentityTablePrefix, StringComparison.Ordinal))
                continue;

            var family = FamilyOf(entityType, families, moduleAssembly);
            // Two entity types sharing one table that belong to different families leave the table's family unknown.
            stamped[table] = stamped.TryGetValue(table, out var existing) && !StringComparer.Ordinal.Equals(existing, family) ? null : family;
        }

        return new ExpandOnlyMigrationFamilies(
            stamped,
            families.Chains.ToDictionary(chain => chain.Family, chain => chain.ReadableVersions, StringComparer.Ordinal));
    }

    private static string? FamilyOf(IReadOnlyEntityType entityType, EfSchemaModuleFamilies families, Assembly moduleAssembly)
    {
        if (families.Chains.Count == 1)
            return families.Chains[0].Family;
        // A snapshot's entity types are property bags that only carry their CLR type's name.
        var type = Resolve(entityType.Name, moduleAssembly) ?? (entityType.HasSharedClrType ? null : entityType.ClrType);
        return type is null ? null : families.FamilyOf(type)?.Family;
    }

    /// <summary>A snapshot's entity type name as a type of the module's assembly, nested types spelt either way.</summary>
    private static Type? Resolve(string name, Assembly moduleAssembly) =>
        moduleAssembly.GetType(name, throwOnError: false) ??
        LoadableTypes(moduleAssembly).FirstOrDefault(type => StringComparer.Ordinal.Equals(type.FullName?.Replace('+', '.'), name));

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException failure)
        {
            return failure.Types.OfType<Type>();
        }
    }
}
