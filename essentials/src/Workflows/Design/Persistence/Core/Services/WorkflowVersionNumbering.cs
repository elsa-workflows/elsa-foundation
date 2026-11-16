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

    /// <summary>
    /// Assesses the version-selection part of a draft promotion. The caller supplies the current
    /// semantic identity lookup result so this pure policy remains reusable by both the read-only
    /// preflight and the locked mutation path.
    /// </summary>
    public static WorkflowPromotionVersionAssessment AssessPromotion(
        string? latestVersion,
        string? requestedVersion,
        bool versionIdentityExists)
    {
        var assignmentMode = requestedVersion is null ? "automatic" : "exact";
        var normalizedRequest = requestedVersion?.Trim();
        var candidate = normalizedRequest ?? NextMajor(latestVersion);

        if (!SemVer.TryParse(candidate, out var candidateSemVer))
        {
            return NotReady(
                assignmentMode,
                normalizedRequest,
                null,
                latestVersion,
                "invalid-version",
                "The requested version must be a valid semantic version.");
        }

        if (latestVersion is not null &&
            SemVer.TryParse(latestVersion, out var latestSemVer) &&
            candidateSemVer <= latestSemVer)
        {
            // Equal precedence is normally a non-forward request. A differently labelled identity
            // (for example 2.0.0+build.7 versus 2.0.0) is the explicit duplicate case required by
            // the contract because SemVer deliberately ignores build metadata for identity.
            if (versionIdentityExists &&
                candidateSemVer == latestSemVer &&
                !string.Equals(candidate, latestVersion, StringComparison.Ordinal))
            {
                return NotReady(
                    assignmentMode,
                    normalizedRequest,
                    null,
                    latestVersion,
                    "version-conflict",
                    "A workflow version with the same semantic identity already exists.");
            }

            return NotReady(
                assignmentMode,
                normalizedRequest,
                null,
                latestVersion,
                "not-forward",
                "The requested version must be greater than the latest immutable version.");
        }

        if (versionIdentityExists)
        {
            return NotReady(
                assignmentMode,
                normalizedRequest,
                null,
                latestVersion,
                "version-conflict",
                "A workflow version with the same semantic identity already exists.");
        }

        return new WorkflowPromotionVersionAssessment(
            true,
            assignmentMode,
            normalizedRequest,
            candidate,
            latestVersion,
            []);
    }

    /// <summary>
    /// Returns the semantic identity key that should be checked after an initial assessment.
    /// Rejected parseable exact requests still need this lookup so build-metadata-only aliases can
    /// be distinguished from ordinary equal/lower non-forward requests.
    /// </summary>
    public static string? GetCandidateIdentitySortKey(WorkflowPromotionVersionAssessment assessment)
    {
        var candidate = assessment.ResolvedVersion ?? assessment.RequestedVersion;
        return SemVer.TryParse(candidate, out var semVer) ? semVer.ToSortKey() : null;
    }

    private static WorkflowPromotionVersionAssessment NotReady(
        string assignmentMode,
        string? requestedVersion,
        string? resolvedVersion,
        string? latestVersion,
        string code,
        string message) =>
        new(false, assignmentMode, requestedVersion, resolvedVersion, latestVersion, [new(code, message)]);
}

/// <summary>One non-mutating evaluation of automatic or exact workflow-version selection.</summary>
public sealed record WorkflowPromotionVersionAssessment(
    bool IsReady,
    string AssignmentMode,
    string? RequestedVersion,
    string? ResolvedVersion,
    string? LatestVersion,
    IReadOnlyCollection<WorkflowPromotionVersionIssue> Issues);

/// <summary>A stable policy issue returned by promotion preflight and reused by promotion errors.</summary>
public sealed record WorkflowPromotionVersionIssue(string Code, string Message);
