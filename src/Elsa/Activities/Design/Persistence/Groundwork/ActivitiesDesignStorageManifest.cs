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
    public const string SchemaVersion = "1.0.0";

    public const string ByCollectionIndex = "by-collection";
    public const string CollectionField = "collection";
    public const string ListAllQuery = "list-all";

    public const string ActivityDefinitionDocumentKind = "activityDefinition";

    /// <summary>Constant partition value stamped on every activity-definition document (see <see cref="ByCollectionIndex"/>).</summary>
    public const string ActivityDefinitionCollection = "activityDefinition";

    public const string ActivityDefinitionVersionDocumentKind = "activityDefinitionVersion";

    /// <summary>Constant partition value stamped on every activity-definition-version document (see <see cref="ByCollectionIndex"/>).</summary>
    public const string ActivityDefinitionVersionCollection = "activityDefinitionVersion";

    public const string ActivityAvailabilitySettingsDocumentKind = "activityAvailabilitySettings";

    /// <summary>Constant partition value stamped on every activity-availability-settings document (see <see cref="ByCollectionIndex"/>).</summary>
    public const string ActivityAvailabilitySettingsCollection = "activityAvailabilitySettings";

    public static StorageManifest Create() => new(
        new StorageManifestIdentity("elsa-activities-design"),
        new StorageManifestOwner("elsa.activities.design"),
        new StorageManifestVersion(SchemaVersion),
        [
            Unit(
                ActivityDefinitionDocumentKind,
                "Activity definition",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query(ListAllQuery, ByCollectionIndex)]),
            Unit(
                ActivityDefinitionVersionDocumentKind,
                "Activity definition version",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query(ListAllQuery, ByCollectionIndex)]),
            Unit(
                ActivityAvailabilitySettingsDocumentKind,
                "Activity availability settings",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query(ListAllQuery, ByCollectionIndex)])
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
        TenancyPolicy.Scoped,
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
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        IndexPhysicalizationPolicy.Optimized);
}
