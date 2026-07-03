namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Builds deterministic, collision-free composite Groundwork document ids from two parts. Each part is
/// escaped so a separator (or a literal escape marker) inside a part can never forge a different
/// composite key. Shared by the bridges that key a document by <c>(workflowExecutionId, subId)</c>.
/// </summary>
internal static class GroundworkCompositeDocumentId
{
    /// <summary>Composes <paramref name="first"/> and <paramref name="second"/> into one escaped, collision-free id.</summary>
    public static string From(string first, string second) =>
        $"{Escape(first)}:{Escape(second)}";

    private static string Escape(string value) => value.Replace("%", "%25").Replace(":", "%3A");
}
