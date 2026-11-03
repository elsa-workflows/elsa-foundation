namespace Elsa.Activities.Design.Core.Models;

/// <summary>The outcome of one <see cref="Contracts.IActivityAuthoringCatalog"/> read.</summary>
/// <param name="Allowed">Whether the actor may read the catalog. A denied result carries no entries.</param>
/// <param name="Entries">The catalog entries in the designer's order: persisted versions, then built-in intrinsics.</param>
public sealed record ActivityAuthoringCatalogResult(bool Allowed, IReadOnlyList<ActivityAuthoringCatalogEntry> Entries)
{
    /// <summary>The result for an actor without catalog read permission.</summary>
    public static ActivityAuthoringCatalogResult Denied { get; } = new(false, []);
}

/// <summary>
/// One activity version the designer offers. <see cref="Available"/> is the designer's addability under the host
/// availability settings; an unavailable entry stays listed so an existing node that uses it remains explainable.
/// </summary>
/// <param name="ActivityVersionId">The exact activity version identity.</param>
/// <param name="ActivityTypeKey">The activity type identity, stable across versions.</param>
/// <param name="Version">The version label.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="Category">The palette category, if any.</param>
/// <param name="Description">The description, if any.</param>
/// <param name="Available">Whether a new node of this version can be added.</param>
/// <param name="AvailabilityReason">Why the version is unavailable, when the designer says so.</param>
/// <param name="Inputs">The declared inputs.</param>
public sealed record ActivityAuthoringCatalogEntry(
    string ActivityVersionId,
    string ActivityTypeKey,
    string Version,
    string DisplayName,
    string? Category,
    string? Description,
    bool Available,
    string? AvailabilityReason,
    IReadOnlyList<ActivityAuthoringCatalogInput> Inputs);

/// <summary>One declared input of a catalog entry. No default value or authored binding is carried.</summary>
/// <param name="ReferenceKey">The stable contract reference key.</param>
/// <param name="Name">The input name.</param>
/// <param name="Type">The input type alias.</param>
/// <param name="DisplayName">The display name, if any.</param>
/// <param name="IsRequired">Whether the input is required.</param>
/// <param name="IsSensitive">Whether the input is sensitive.</param>
/// <param name="IsCredential">Whether the input accepts only a secret reference or no binding.</param>
public sealed record ActivityAuthoringCatalogInput(
    string ReferenceKey,
    string Name,
    string Type,
    string? DisplayName,
    bool IsRequired,
    bool IsSensitive,
    bool IsCredential)
{
    /// <summary>The input's editor hint (for example <c>password</c>), when it declares one.</summary>
    public string? UiHint { get; init; }

    /// <summary>
    /// Whether the designer masks the input's value: it is sensitive, a credential, or hinted as a password. A masked
    /// value is never shown, logged or disclosed.
    /// </summary>
    public bool IsMasked => IsSensitive || IsCredential || string.Equals(UiHint, "password", StringComparison.OrdinalIgnoreCase);
}
