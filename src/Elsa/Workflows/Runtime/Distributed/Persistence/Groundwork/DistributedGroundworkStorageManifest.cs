using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;

/// <summary>
/// Declares the Groundwork storage manifest for the durable distributed-runtime stores. The distributed provider is
/// an opt-in leaf, so its persistence bridge owns its own manifest (mirroring the Identity and Secrets Groundwork
/// precedents) rather than folding its document kinds into the runtime document manifest. The document-kind literals
/// themselves are owned by the leaf's <see cref="DistributedRuntimeStorageManifest"/> — the leaf stays the wire-safe
/// naming authority and this bridge only consumes the constants.
/// </summary>
/// <remarks>
/// Both units declare <c>ConcurrencyPolicy.Optimistic()</c>: the stores lean on the provider's
/// <c>ExpectedVersion</c> compare-and-swap (including the <c>0</c> = create-only claim) for every mutation, so
/// cross-node races are decided by the document store's storage layer, never by read-check-write in bridge code.
/// </remarks>
public static class DistributedGroundworkStorageManifest
{
    public const string SchemaVersion = "1.0.0";

    public const string ByWorkflowExecutionIndex = "by-workflow-execution";
    public const string ByCollectionIndex = "by-collection";
    public const string WorkflowExecutionIdField = "workflowExecutionId";
    public const string CollectionField = "collection";

    public static StorageManifest Create() => new(
        new StorageManifestIdentity("elsa-workflows-runtime-distributed"),
        new StorageManifestOwner("elsa.workflows.runtime.distributed"),
        new StorageManifestVersion(SchemaVersion),
        [
            Unit(
                DistributedRuntimeStorageManifest.ExecutionPlacementDocumentKind,
                "Execution placement lease",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query("list-all", ByCollectionIndex)]),
            Unit(
                DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind,
                "Execution command transport item",
                [
                    Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField),
                    Keyword(ByCollectionIndex, CollectionField)
                ],
                [
                    Query("list-by-workflow-execution", ByWorkflowExecutionIndex),
                    Query("list-all", ByCollectionIndex)
                ])
        ],
        new HashSet<string> { "schema-history", "optimistic-concurrency" },
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
        TenancyPolicy.None,
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
