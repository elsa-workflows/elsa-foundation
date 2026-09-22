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
/// Call this <em>before</em> the integrity checks, not inside them. The schema version answers "can
/// this build read this row at all", which has to be settled before "is this row internally
/// consistent" means anything: a row from a version whose shape differs may legitimately fail an
/// integrity check that only describes the shape this build writes.
/// </para>
/// </remarks>
public static class EfSchemaVersion
{
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
