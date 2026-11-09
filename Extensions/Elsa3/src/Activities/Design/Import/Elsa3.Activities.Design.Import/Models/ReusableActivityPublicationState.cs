using Elsa.Activities.Design.Core.Models;

namespace Elsa3.Activities.Design.Import.Models;

/// <summary>Where one imported reusable activity version stands after the publish phase (spec 246 R-5, R-6).</summary>
public enum ReusableActivityPublicationStatus
{
    /// <summary>This apply published it.</summary>
    Published,

    /// <summary>It already had an active publication under the plan's version id and version.</summary>
    AlreadyPublished,

    /// <summary>The publisher refused it, or its id is held by a publication of another version.</summary>
    Refused,

    /// <summary>Not attempted: a version it depends on is not published.</summary>
    NotPublishedDependency,

    /// <summary>A status read found no active publication; only status reads report it.</summary>
    NotPublished
}

/// <summary>
/// The publication state of one selected reusable version. Reported by the apply, a replay and a status read; never
/// stored on the receipt, so it always reflects the publication store.
/// </summary>
/// <param name="ErrorCode">
/// For <see cref="ReusableActivityPublicationStatus.Refused"/>: the publisher's code (or the import's own, such as
/// <c>ELS3-REUSABLE-IDENTITY-COLLISION</c>); for <see cref="ReusableActivityPublicationStatus.NotPublishedDependency"/>:
/// <c>ELS3-REUSABLE-DEPENDENCY-NOT-PUBLISHED</c>.
/// </param>
/// <param name="Diagnostics">What went wrong and what to change, for a version that is not published.</param>
/// <param name="BlockedBy">For <see cref="ReusableActivityPublicationStatus.NotPublishedDependency"/>: the dependency's version id.</param>
public sealed record ReusableActivityPublicationState(
    string ActivityDefinitionVersionId,
    string SourceVersionId,
    ReusableActivityPublicationStatus Status,
    string? ErrorCode = null,
    IReadOnlyList<ActivityDiagnostic>? Diagnostics = null,
    string? BlockedBy = null);
