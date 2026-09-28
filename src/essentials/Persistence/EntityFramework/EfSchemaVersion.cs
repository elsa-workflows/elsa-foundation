using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The one place a persisted row's schema version is checked against what this build reads: its family's
/// <see cref="EfSchemaChain"/>, resolved from the family's declaration.
/// </summary>
/// <remarks>
/// Every EF store checks a row's integrity before trusting it: identity hashes, order keys, revision, and the family's
/// schema version. Those checks were fused into one compound condition, so a row written by a build this host does not
/// run was reported as corrupt. ADR 0077 separates them, because they are different events with different operator
/// responses.
/// <para>
/// A call site checks the version <em>before</em> anything else reads the row (spec 180, FR-006): the schema version
/// answers "can this build read this row at all", which has to be settled before "is this row internally consistent"
/// means anything, since a row from a version whose shape differs may legitimately fail an integrity check that only
/// describes the shape this build writes. Every call site evaluates its <see cref="Readable"/>,
/// <see cref="NotReadable"/> or <see cref="EnsureReadable"/> term first, and an architecture guard fails the build when
/// one does not. No call site states the versions it accepts: they come from the family's declaration, so the set a
/// read accepts is the set the host reports (spec 183, FR-020).
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
    /// Throws <see cref="EfSchemaVersionSkewException"/> when <paramref name="found"/> is not in
    /// <paramref name="family"/>'s readable set.
    /// </summary>
    public static void EnsureReadable(EfSchemaChain family, string? found)
    {
        ArgumentNullException.ThrowIfNull(family);
        family.EnsureReadable(found);
    }

    /// <summary>
    /// Throws <see cref="EfSchemaVersionSkewException"/> unless <paramref name="found"/> is <paramref name="family"/>'s
    /// current version. For a read path that cannot apply the chain, such as EF materializing domain types directly: it
    /// accepts nothing it would have to upcast, and names only the version it does accept.
    /// </summary>
    public static void EnsureCurrent(EfSchemaChain family, string? found)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (!StringComparer.Ordinal.Equals(found, family.CurrentVersion))
            throw new EfSchemaVersionSkewException(family.Family, found, family.CurrentVersion, [family.CurrentVersion]);
    }

    /// <summary>
    /// True when <paramref name="found"/> is in <paramref name="family"/>'s readable set. For call sites that must
    /// decide rather than throw; the diagnosis stays the same either way.
    /// </summary>
    public static bool IsReadable(EfSchemaChain family, string? found)
    {
        ArgumentNullException.ThrowIfNull(family);
        return family.IsReadable(found);
    }

    /// <summary>
    /// Throws on skew, otherwise returns <c>true</c>. Drop-in for a positive clause in an integrity check
    /// (<c>Readable(...) &amp;&amp; …</c>), so the surrounding condition keeps its shape and short-circuit order while
    /// the diagnosis changes.
    /// </summary>
    public static bool Readable(EfSchemaChain family, string? found)
    {
        EnsureReadable(family, found);
        return true;
    }

    /// <summary>
    /// Throws on skew, otherwise returns <c>false</c>. Drop-in for a negative clause in an integrity check
    /// (<c>NotReadable(...) || …</c>).
    /// </summary>
    public static bool NotReadable(EfSchemaChain family, string? found)
    {
        EnsureReadable(family, found);
        return false;
    }
}
