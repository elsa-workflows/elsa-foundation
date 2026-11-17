namespace Elsa.Workflows.Design.Core.Services;

/// <summary>
/// The stable diagnostic codes of script variable reads (spec 1222, data model "Diagnostics"). <c>VF-READ-001</c>, a
/// computed name that matches nothing at run time, is recorded on the input snapshot's computed read. No code is
/// reserved for a possibly protected or transient candidate: an authored variable declaration can be neither today, so a
/// computed read is checked for the variable it actually reads, at run time.
/// </summary>
public static class ScriptVariableReadDiagnostics
{
    /// <summary>A literal name resolves to no visible declaration (FR-012, FR-023): publication is refused.</summary>
    public const string UnresolvedLiteralRead = "VF-READ-002";

    /// <summary>A literal name resolves to a transient variable (FR-017): publication is refused.</summary>
    public const string TransientLiteralRead = "VF-READ-003";

    /// <summary>A script reads variables by a computed or opaque name; the message lists its candidates (FR-009).</summary>
    public const string ComputedReadReported = "VF-READ-004";

    /// <summary>A literal name now resolves to a different declaration than in the published version (FR-024).</summary>
    public const string ShadowChange = "VF-READ-005";
}
