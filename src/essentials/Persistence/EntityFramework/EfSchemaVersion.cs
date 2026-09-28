using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The one place a persisted row's schema version is compared against the version this build reads.
/// </summary>
/// <remarks>
/// Every EF store checks a row's envelope before trusting it: identity hashes, order keys, revision,
/// and the module's schema version. Those checks were fused into one compound condition, so a row
/// written by a module version this host does not run was reported as a corrupt envelope. ADR 0077
/// separates them, because they are different events with different operator responses.
/// <para>
/// A call site checks the version <em>before</em> the integrity checks, not alongside them. The
/// schema version answers "can this build read this row at all", which has to be settled before
/// "is this row internally consistent" means anything: a row from a version whose shape differs may
/// legitimately fail an integrity check that only describes the shape this build writes. Every call
/// site evaluates its <see cref="Readable"/> or <see cref="NotReadable"/> term first, and an
/// architecture guard fails the build when a new one does not.
/// </para>
/// </remarks>
public static class EfSchemaVersion
{
    /// <summary>The column every stamped table carries its row's schema version in.</summary>
    public const string ColumnName = "SchemaVersion";

    /// <summary>
    /// Gives the <see cref="ColumnName"/> stamp of every table <paramref name="modelBuilder"/> maps a non-unique index
    /// under EF's default name, so the rows still at a given version are found without scanning the table. A module's
    /// context calls this last in <c>OnModelCreating</c>, once every table and its stamp are configured.
    /// </summary>
    public static ModelBuilder IndexSchemaVersionStamps(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        var unindexed = modelBuilder.Model.GetEntityTypes()
            .Where(entityType => entityType.GetTableName() is not null)
            .Select(entityType => (EntityType: entityType, Stamp: entityType.FindProperty(ColumnName)))
            .Where(table => table.Stamp is not null && table.EntityType.FindIndex(table.Stamp) is null)
            .ToList();
        foreach (var (entityType, stamp) in unindexed)
            entityType.AddIndex(stamp!);
        return modelBuilder;
    }

    /// <summary>
    /// Throws <see cref="EfSchemaVersionSkewException"/> when <paramref name="found"/> is not the
    /// version this build reads.
    /// </summary>
    public static void EnsureReadable(string module, string? found, string expected)
    {
        if (!StringComparer.Ordinal.Equals(found, expected))
            throw new EfSchemaVersionSkewException(module, found, expected);
    }

    /// <summary>
    /// True when <paramref name="found"/> is the version this build reads. For call sites that must
    /// decide rather than throw; the diagnosis stays the same either way.
    /// </summary>
    public static bool IsReadable(string? found, string expected) =>
        StringComparer.Ordinal.Equals(found, expected);

    /// <summary>
    /// Throws on skew, otherwise returns <c>true</c>. Drop-in for a positive clause in an envelope
    /// check (<c>row.SchemaVersion == Module.SchemaVersion &amp;&amp; …</c>), so the surrounding
    /// condition keeps its shape and short-circuit order while the diagnosis changes.
    /// </summary>
    public static bool Readable(string module, string? found, string expected)
    {
        EnsureReadable(module, found, expected);
        return true;
    }

    /// <summary>
    /// Throws on skew, otherwise returns <c>false</c>. Drop-in for a negative clause in an envelope
    /// check (<c>row.SchemaVersion != Module.SchemaVersion || …</c>).
    /// </summary>
    public static bool NotReadable(string module, string? found, string expected)
    {
        EnsureReadable(module, found, expected);
        return false;
    }
}
