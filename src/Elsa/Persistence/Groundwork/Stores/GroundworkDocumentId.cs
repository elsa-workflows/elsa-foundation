namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Builds deterministic, collision-free composite document ids from the runtime key parts. Parts are
/// escaped so a separator inside any single part cannot forge a different key tuple.
/// </summary>
internal static class GroundworkDocumentId
{
    public static string Compose(params string[] parts) =>
        string.Join(":", parts.Select(Escape));

    private static string Escape(string value) => value.Replace("%", "%25").Replace(":", "%3A");
}
