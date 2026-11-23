namespace Elsa.Expressions.JavaScript.Core.Contracts;

/// <summary>
/// The names of the built-in .NET-implemented functions of every JavaScript surface (spec 1223 FR-006, FR-007, FR-026)
/// and the read roots, which the function naming rule reserves (FR-027, ADR 0045 §5 D-5). The engine registers the
/// implementations; publish and the analyzers use the names. There is no <c>toJson</c>: scripts serialize with
/// <c>JSON.stringify</c> (FR-009).
/// </summary>
public static class JavaScriptBuiltInFunctions
{
    public const string NewGuid = "newGuid";
    public const string NewGuidString = "newGuidString";
    public const string GetGuidString = "getGuidString";
    public const string NewShortGuid = "newShortGuid";
    public const string GetShortGuid = "getShortGuid";
    public const string ParseGuid = "parseGuid";
    public const string StringToBase64 = "stringToBase64";
    public const string StringFromBase64 = "stringFromBase64";

    /// <summary>The read roots a script reads engine-owned values through (ADR 0089, ADR 0045 §5 D-5).</summary>
    public static IReadOnlyList<string> ReadRoots { get; } = ["variables", "input", "activities", "workflow", "args"];

    /// <summary>The built-in function names.</summary>
    public static IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        NewGuid, NewGuidString, GetGuidString, NewShortGuid, GetShortGuid, ParseGuid, StringToBase64, StringFromBase64,
    };
}
