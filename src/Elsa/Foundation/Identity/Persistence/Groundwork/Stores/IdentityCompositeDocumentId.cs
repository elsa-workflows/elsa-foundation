namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Builds deterministic, collision-free composite Groundwork document ids from identity key parts. Each
/// part is lower-cased (identity lookups are case-insensitive, matching the in-memory store's
/// <c>OrdinalIgnoreCase</c> semantics) and escaped so a separator (or a literal escape marker) inside a
/// part can never forge a different composite key. This mirrors the runtime bridge's composite-id helper
/// without taking a cross-domain project reference on it.
/// </summary>
public static class IdentityCompositeDocumentId
{
    public static string From(params string[] parts) =>
        string.Join(':', parts.Select(Escape));

    /// <summary>Normalizes a single key part for case-insensitive index/equality lookups.</summary>
    public static string Normalize(string? value) =>
        IdentityUnicodeCaseCompatibility.ToLowerInvariant(value ?? string.Empty);

    private static string Escape(string value) =>
        Normalize(value).Replace("%", "%25").Replace(":", "%3A");
}
