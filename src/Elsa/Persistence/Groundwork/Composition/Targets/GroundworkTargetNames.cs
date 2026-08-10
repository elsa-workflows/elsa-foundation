namespace Elsa.Persistence.Groundwork.Targets;

/// <summary>
/// Canonicalizes Groundwork target names. A target name is an opaque, operator-chosen label for one
/// admitted physical store; it carries no lane, domain, or provider meaning. <see cref="Default"/> is
/// the only well-known name, and it is what every domain binds to unless a host names another target.
/// </summary>
public static class GroundworkTargetNames
{
    /// <summary>The target a domain binds to when its feature declares no explicit target.</summary>
    public const string Default = "default";

    /// <summary>Whether <paramref name="name"/> canonicalizes to <see cref="Default"/>.</summary>
    public static bool IsDefault(string? name) =>
        string.Equals(Normalize(name), Default, StringComparison.Ordinal);

    /// <summary>
    /// Canonicalizes an optionally-configured target name. Absent, empty, and whitespace-only names all
    /// mean <see cref="Default"/>; every other name is trimmed and compared ordinally thereafter, so
    /// <c>"Design"</c> and <c>"design"</c> are two distinct targets.
    /// </summary>
    public static string Normalize(string? name) =>
        string.IsNullOrWhiteSpace(name) ? Default : name.Trim();
}
