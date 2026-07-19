using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork;

public static class PublishingGroundworkStorageManifest
{
    public const string SchemaVersion = "1.0.0";
    public const string PublicationSlotDocumentKind = "publishingPublicationSlot";
    public const string PublicationRecordDocumentKind = "publishingPublicationRecord";
    public const string PublicationPolicyDocumentKind = "publishingPublicationPolicy";
    public const string ProjectionIntentDocumentKind = "publishingProjectionIntent";
    public const string SnapshotReviewDocumentKind = "publishingSnapshotReview";
    public const string ActivityPublicationReceiptDocumentKind = "publishingActivityPublicationReceipt";
    public const string ActivityDraftTestRunDocumentKind = "publishingActivityDraftTestRun";
    public const string ByDefinitionIndex = "by-definition";
    public const string BySlotIndex = "by-slot";
    public const string ByPublicationIndex = "by-publication";
    public const string ByActivePublicationIndex = "by-active-publication";
    public const string ByExpiresAtIndex = "by-expires-at";
    public const string ListByDefinitionQuery = "list-by-definition";
    public const string FindByActivePublicationQuery = "find-by-active-publication";
    public const string ListBySlotQuery = "list-by-slot";
    public const string ListByPublicationQuery = "list-by-publication";
    public const string DeleteExpiredQuery = "delete-expired";
    public const string WorkflowDefinitionIdField = "workflowDefinitionId";
    public const string SlotIdField = "slotId";
    public const string PublicationIdField = "publicationId";
    public const string ActivePublicationIdField = "slot.activePublicationId";
    public const string ExpiresAtField = "expiresAt";
    public const string ReceiptExpiresAtField = "receiptExpiresAt";

    public static StorageManifest Create() => new(
        new StorageManifestIdentity("elsa-workflows-publishing"),
        new StorageManifestOwner("elsa.workflows.publishing"),
        new StorageManifestVersion(SchemaVersion),
        [
            Unit(PublicationSlotDocumentKind, "Publication slot",
                [Keyword(ByDefinitionIndex, WorkflowDefinitionIdField), Keyword(ByActivePublicationIndex, ActivePublicationIdField)],
                [Query(ListByDefinitionQuery, ByDefinitionIndex), Query(FindByActivePublicationQuery, ByActivePublicationIndex)]),
            Unit(PublicationRecordDocumentKind, "Publication record", [Keyword(BySlotIndex, SlotIdField)], [Query(ListBySlotQuery, BySlotIndex)]),
            Unit(PublicationPolicyDocumentKind, "Publication policy", [], []),
            Unit(ProjectionIntentDocumentKind, "Publication projection intent", [Keyword(ByPublicationIndex, PublicationIdField)], [Query(ListByPublicationQuery, ByPublicationIndex)]),
            Unit(
                SnapshotReviewDocumentKind,
                "Publication snapshot review",
                [DateTime(ByExpiresAtIndex, ExpiresAtField)],
                [Query(
                    DeleteExpiredQuery,
                    ByExpiresAtIndex,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual },
                    QuerySortSupport.Ascending)]),
            Unit(ActivityPublicationReceiptDocumentKind, "Activity publication receipt", [], []),
            Unit(
                ActivityDraftTestRunDocumentKind,
                "Activity draft Test Run receipt",
                [DateTime(ByExpiresAtIndex, ReceiptExpiresAtField)],
                [Query(
                    DeleteExpiredQuery,
                    ByExpiresAtIndex,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual },
                    QuerySortSupport.Ascending)])
        ],
        new HashSet<string> { "schema-history", "optimistic-concurrency" },
        []);

    private static StorageUnit Unit(string kind, string label, IndexDeclaration[] indexes, PortableQueryDeclaration[] queries) => new(
        new StorageUnitIdentity(kind), label, StorageIntent.PortableDocument(), LifecyclePolicy.Mutable,
        IdentityPolicy.StringId(), TenancyPolicy.Scoped, ConcurrencyPolicy.Optimistic(), SerializationPolicy.Json(),
        indexes, queries, PhysicalizationPolicy.Portable);

    private static PortableQueryDeclaration Query(
        string name,
        string index,
        IReadOnlySet<PortableQueryOperation>? operations = null,
        QuerySortSupport sortSupport = QuerySortSupport.None) => new(
        name,
        index,
        operations ?? new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        sortSupport,
        QueryPagingSupport.Offset);

    private static IndexDeclaration Keyword(string identity, string field) => new(
        identity, [new IndexField(field)], IndexValueKind.Keyword, false, true,
        MissingValueBehavior.Excluded, new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        IndexPhysicalizationPolicy.Optimized);

    private static IndexDeclaration DateTime(string identity, string field) => new(
        identity, [new IndexField(field)], IndexValueKind.DateTime, false, true,
        MissingValueBehavior.Excluded,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal, PortableQueryOperation.LessThanOrEqual },
        IndexPhysicalizationPolicy.Optimized);
}
