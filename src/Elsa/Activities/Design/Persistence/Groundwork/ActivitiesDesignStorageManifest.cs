using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;

namespace Elsa.Activities.Design.Persistence.Groundwork;

/// <summary>
/// Provider-neutral Groundwork storage manifest describing the activity <b>design</b> document kinds. It is
/// the activities-lane counterpart of <c>WorkflowsDesignStorageManifest</c>: a host selects the concrete
/// provider (SQLite, SQL Server, PostgreSQL, MongoDB, ...) without changing this description, so the same
/// host-selected document provider can back every Elsa module.
/// <para>
/// Each unit declares a <b>by-collection keyword index</b> — equality on a constant partition value stamped
/// on every document of the kind — letting the closed <c>Query&lt;TEntity&gt;</c> spec enumerate a kind
/// through Groundwork's universally-supported equality contract; the richer operators (IN, substring, OR,
/// ordering) are applied by <c>GroundworkReadStore&lt;TEntity&gt;</c>'s in-memory fallback until Groundwork
/// ships the capability-spec uplift.
/// </para>
/// </summary>
public static class ActivitiesDesignStorageManifest
{
    // Frozen legacy stamp. Groundwork physicalizes additive document kinds/indexes from the manifest;
    // changing this value is not a migration mechanism and would make existing envelopes unreadable.
    public const string SchemaVersion = "1.0.0";

    public const string ByCollectionIndex = "by-collection";
    public const string ByDefinitionIndex = "by-definition";
    public const string ByHeadVersionIndex = "by-head-version";
    public const string ByDraftIndex = "by-draft";
    public const string ByDefinitionVersionIndex = "by-definition-version";
    public const string ByOwnerVersionIndex = "by-owner-version";
    public const string ByDependencyVersionIndex = "by-dependency-version";
    public const string CollectionField = "collection";
    public const string DefinitionIdField = "entity.definitionId";
    public const string HeadVersionIdField = "entity.headVersionId";
    public const string DraftIdField = "entity.draftId";
    public const string DefinitionVersionIdField = "entity.definitionVersionId";
    public const string OwnerVersionIdField = "entity.ownerVersionId";
    public const string DependencyVersionIdField = "entity.dependencyVersionId";

    public const string ActivityDefinitionDocumentKind = "activityDefinition";

    /// <summary>Constant partition value stamped on every activity-definition document (see <see cref="ByCollectionIndex"/>).</summary>
    public const string ActivityDefinitionCollection = "activityDefinition";

    public const string ActivityDefinitionVersionDocumentKind = "activityDefinitionVersion";

    /// <summary>Constant partition value stamped on every activity-definition-version document (see <see cref="ByCollectionIndex"/>).</summary>
    public const string ActivityDefinitionVersionCollection = "activityDefinitionVersion";

    public const string ActivityAvailabilitySettingsDocumentKind = "activityAvailabilitySettings";

    /// <summary>Constant partition value stamped on every activity-availability-settings document (see <see cref="ByCollectionIndex"/>).</summary>
    public const string ActivityAvailabilitySettingsCollection = "activityAvailabilitySettings";

    public const string ActivityDefinitionAuthoringStateDocumentKind = "activityDefinitionAuthoringState";
    public const string ActivityDefinitionAuthoringStateCollection = "activityDefinitionAuthoringState";

    public const string ActivityDefinitionDraftDocumentKind = "activityDefinitionDraft";
    public const string ActivityDefinitionDraftCollection = "activityDefinitionDraft";

    public const string ActivityDefinitionDraftLayoutDocumentKind = "activityDefinitionDraftLayout";
    public const string ActivityDefinitionDraftLayoutCollection = "activityDefinitionDraftLayout";

    public const string ActivityDraftValidationDocumentKind = "activityDraftValidation";
    public const string ActivityDraftValidationCollection = "activityDraftValidation";

    public const string ActivityDefinitionVersionPublicationDocumentKind = "activityDefinitionVersionPublication";
    public const string ActivityDefinitionVersionPublicationCollection = "activityDefinitionVersionPublication";

    public const string ActivityDefinitionVersionLayoutDocumentKind = "activityDefinitionVersionLayout";
    public const string ActivityDefinitionVersionLayoutCollection = "activityDefinitionVersionLayout";

    public const string ActivityDependencyEdgeDocumentKind = "activityDependencyEdge";
    public const string ActivityDependencyEdgeCollection = "activityDependencyEdge";

    public const string ActivityDependencyProjectionDocumentKind = "activityDependencyProjection";
    public const string ActivityDependencyProjectionCollection = "activityDependencyProjection";

    public const string ActivityUpgradePlanDocumentKind = "activityUpgradePlan";
    public const string ActivityUpgradePlanCollection = "activityUpgradePlan";

    public static StorageManifest Create() => new(
        new StorageManifestIdentity("elsa-activities-design"),
        new StorageManifestOwner("elsa.activities.design"),
        new StorageManifestVersion(SchemaVersion),
        [
            Unit(
                ActivityDefinitionDocumentKind,
                "Activity definition",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query("list-all", ByCollectionIndex)]),
            Unit(
                ActivityDefinitionVersionDocumentKind,
                "Activity definition version",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query("list-all", ByCollectionIndex)]),
            Unit(
                ActivityAvailabilitySettingsDocumentKind,
                "Activity availability settings",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query("list-all", ByCollectionIndex)]),
            Unit(
                ActivityDefinitionAuthoringStateDocumentKind,
                "Activity definition authoring state",
                [
                    Keyword(ByCollectionIndex, CollectionField),
                    Keyword(ByDefinitionIndex, DefinitionIdField),
                    Keyword(ByHeadVersionIndex, HeadVersionIdField)
                ],
                [
                    Query("list-all", ByCollectionIndex),
                    Query("list-by-definition", ByDefinitionIndex),
                    Query("list-by-head-version", ByHeadVersionIndex)
                ]),
            Unit(
                ActivityDefinitionDraftDocumentKind,
                "Activity definition draft",
                [Keyword(ByCollectionIndex, CollectionField), Keyword(ByDefinitionIndex, DefinitionIdField)],
                [Query("list-all", ByCollectionIndex), Query("list-by-definition", ByDefinitionIndex)]),
            Unit(
                ActivityDefinitionDraftLayoutDocumentKind,
                "Activity definition draft layout",
                [Keyword(ByCollectionIndex, CollectionField), Keyword(ByDraftIndex, DraftIdField)],
                [Query("list-all", ByCollectionIndex), Query("list-by-draft", ByDraftIndex)]),
            Unit(
                ActivityDraftValidationDocumentKind,
                "Activity draft validation",
                [Keyword(ByCollectionIndex, CollectionField), Keyword(ByDraftIndex, DraftIdField)],
                [Query("list-all", ByCollectionIndex), Query("list-by-draft", ByDraftIndex)]),
            Unit(
                ActivityDefinitionVersionPublicationDocumentKind,
                "Activity definition version publication",
                [
                    Keyword(ByCollectionIndex, CollectionField),
                    Keyword(ByDefinitionIndex, DefinitionIdField),
                    Keyword(ByDefinitionVersionIndex, DefinitionVersionIdField)
                ],
                [
                    Query("list-all", ByCollectionIndex),
                    Query("list-by-definition", ByDefinitionIndex),
                    Query("list-by-definition-version", ByDefinitionVersionIndex)
                ]),
            Unit(
                ActivityDefinitionVersionLayoutDocumentKind,
                "Activity definition version layout",
                [Keyword(ByCollectionIndex, CollectionField), Keyword(ByDefinitionVersionIndex, DefinitionVersionIdField)],
                [Query("list-all", ByCollectionIndex), Query("list-by-definition-version", ByDefinitionVersionIndex)]),
            Unit(
                ActivityDependencyEdgeDocumentKind,
                "Activity dependency edge",
                [
                    Keyword(ByCollectionIndex, CollectionField),
                    Keyword(ByOwnerVersionIndex, OwnerVersionIdField),
                    Keyword(ByDependencyVersionIndex, DependencyVersionIdField)
                ],
                [
                    Query("list-all", ByCollectionIndex),
                    Query("list-by-owner-version", ByOwnerVersionIndex),
                    Query("list-by-dependency-version", ByDependencyVersionIndex)
                ]),
            Unit(
                ActivityDependencyProjectionDocumentKind,
                "Activity dependency projection",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query("list-all", ByCollectionIndex)]),
            Unit(
                ActivityUpgradePlanDocumentKind,
                "Activity upgrade plan",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query("list-all", ByCollectionIndex)])
        ],
        new HashSet<string> { "optimistic-concurrency" },
        []);

    private static StorageUnit Unit(
        string documentKind,
        string label,
        IndexDeclaration[] indexes,
        PortableQueryDeclaration[] queries) => new(
        new StorageUnitIdentity(documentKind),
        label,
        StorageIntent.PortableDocument(),
        LifecyclePolicy.Mutable,
        IdentityPolicy.StringId(),
        TenancyPolicy.Global,
        ConcurrencyPolicy.Optimistic(),
        SerializationPolicy.Json(),
        indexes,
        queries,
        PhysicalizationPolicy.Portable);

    private static PortableQueryDeclaration Query(string name, string indexName) => new(
        name,
        indexName,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        QuerySortSupport.None,
        QueryPagingSupport.Offset);

    private static IndexDeclaration Keyword(string identity, string field) => new(
        identity,
        [new IndexField(field)],
        IndexValueKind.Keyword,
        false,
        true,
        MissingValueBehavior.Excluded,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal });
}
