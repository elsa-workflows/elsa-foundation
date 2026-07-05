using Elsa.Primitives.Versioning;

namespace Elsa.Workflows.Design.Persistence.Core.Services;

/// <summary>
/// The single home of the workflow version-numbering policy: each published workflow version is a new
/// major (1.0.0 → 2.0.0 → …). Previously duplicated verbatim across the add-version handler and both
/// promote-draft commands (issue #417 item 6); a future policy change edits exactly one method.
/// </summary>
public static class WorkflowVersionNumbering
{
    /// <summary>
    /// Computes the version string for the next published version given the current latest version
    /// (<c>null</c> or unparseable → "1.0.0").
    /// </summary>
    public static string NextMajor(string? lastVersion) =>
        lastVersion is not null && SemVer.TryParse(lastVersion, out var semVer)
            ? $"{semVer.Major + 1}.0.0"
            : "1.0.0";
}
