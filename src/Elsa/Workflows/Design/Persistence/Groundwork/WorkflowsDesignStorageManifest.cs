using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;

namespace Elsa.Workflows.Design.Persistence.Groundwork;

/// <summary>
/// Provider-neutral Groundwork storage manifest describing the workflow <b>design</b> document kinds.
/// It mirrors <c>ElsaRuntimeStorageManifest</c> for the design lane: a host selects the concrete provider
/// (SQLite, SQL Server, PostgreSQL, MongoDB, ...) without changing this description, so the same
/// host-selected document provider can back every Elsa module.
/// <para>
/// Each design unit declares a <b>by-collection keyword index</b> — equality on a constant partition value
/// stamped on every document of the kind. Groundwork's portable document query supports equality-on-index
/// today, so this index lets the closed <c>Query&lt;TEntity&gt;</c> spec enumerate a kind through the
/// universally-supported equality contract; the richer operators (IN, substring, OR, ordering) are applied
/// by <c>GroundworkReadStore&lt;TEntity&gt;</c>'s in-memory fallback until Groundwork ships the
/// capability-spec uplift, at which point native index declarations can be added without changing the ports.
/// </para>
/// </summary>
public static class WorkflowsDesignStorageManifest
{
    public const string SchemaVersion = "1.0.0";

    public const string ByCollectionIndex = "by-collection";
    public const string CollectionField = "collection";
    public const string ListAllQuery = "list-all";

    public const string WorkflowDefinitionDocumentKind = "workflowDefinition";

    /// <summary>Constant partition value stamped on every workflow-definition document (see <see cref="ByCollectionIndex"/>).</summary>
    public const string WorkflowDefinitionCollection = "workflowDefinition";

    public const string WorkflowDefinitionVersionDocumentKind = "workflowDefinitionVersion";

    /// <summary>Constant partition value stamped on every workflow-definition-version document (see <see cref="ByCollectionIndex"/>).</summary>
    public const string WorkflowDefinitionVersionCollection = "workflowDefinitionVersion";

    public const string WorkflowDefinitionDraftDocumentKind = "workflowDefinitionDraft";

    /// <summary>Constant partition value stamped on every workflow-definition-draft document (see <see cref="ByCollectionIndex"/>).</summary>
    public const string WorkflowDefinitionDraftCollection = "workflowDefinitionDraft";

    public const string WorkflowDefinitionVersionLayoutDocumentKind = "workflowDefinitionVersionLayout";

    /// <summary>Constant partition value stamped on every workflow-definition-version-layout document (see <see cref="ByCollectionIndex"/>).</summary>
    public const string WorkflowDefinitionVersionLayoutCollection = "workflowDefinitionVersionLayout";

    public static StorageManifest Create() => new(
        new StorageManifestIdentity("elsa-workflows-design"),
        new StorageManifestOwner("elsa.workflows.design"),
        new StorageManifestVersion(SchemaVersion),
        [
            Unit(
                WorkflowDefinitionDocumentKind,
                "Workflow definition",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query(ListAllQuery, ByCollectionIndex)]),
            Unit(
                WorkflowDefinitionVersionDocumentKind,
                "Workflow definition version",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query(ListAllQuery, ByCollectionIndex)]),
            Unit(
                WorkflowDefinitionDraftDocumentKind,
                "Workflow definition draft",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query(ListAllQuery, ByCollectionIndex)]),
            Unit(
                WorkflowDefinitionVersionLayoutDocumentKind,
                "Workflow definition version layout",
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
