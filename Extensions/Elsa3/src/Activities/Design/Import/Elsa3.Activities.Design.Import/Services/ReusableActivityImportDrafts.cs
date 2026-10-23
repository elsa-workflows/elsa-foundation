using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa3.Activities.Design.Import.Models;

namespace Elsa3.Activities.Design.Import.Services;

/// <summary>
/// The Design-owned draft an imported reusable version is published from (spec 246 R-4): the graph manifest as the
/// provider payload and the converted contract, at revision 1 with an empty layout. Ids derive from the Elsa 3 source
/// version, so the commit and a resumed publish phase build the same draft.
/// </summary>
public static class ReusableActivityImportDrafts
{
    /// <summary>The graph activity provider's key; the import writes its schema-2 manifest.</summary>
    public const string GraphProviderKey = "elsa.activity-graph";

    public const string GraphProviderSchemaVersion = "2";

    /// <param name="headVersionId">The definition head the draft starts from, or null for its first version.</param>
    public static ActivityDefinitionDraft Draft(ImportedReusableActivityVersion version, string? headVersionId) => new()
    {
        Id = version.DraftId,
        TenantId = version.TenantId,
        DefinitionId = version.DefinitionId,
        Revision = 1,
        SourceVersionId = headVersionId,
        PresentationLabel = $"Elsa 3 v{version.Version.Split('.')[0]}",
        Status = ActivityDefinitionDraftStatus.Active,
        State = new(
            version.Contract,
            new ActivityProviderManifest(GraphProviderKey, GraphProviderSchemaVersion, version.GraphManifest.Clone()),
            new Dictionary<string, string>()),
        CreatedAt = version.CreatedAt,
        LastModifiedAt = version.CreatedAt
    };

    /// <summary>Elsa 3 exports carry no designer layout the graph activity can use, so the draft has none.</summary>
    public static ActivityDefinitionDraftLayout Layout(ImportedReusableActivityVersion version) => new()
    {
        Id = ReusableActivityImportIdentity.Create("activity-draft-layout", version.SourceVersionId),
        TenantId = version.TenantId,
        DraftId = version.DraftId,
        Revision = 1,
        Records = [],
        CreatedAt = version.CreatedAt,
        LastModifiedAt = version.CreatedAt
    };
}
