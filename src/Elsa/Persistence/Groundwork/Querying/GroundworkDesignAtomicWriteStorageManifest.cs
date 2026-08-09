using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Owns the scope-bound operation ledger shared by workflow and activity design persistence, and the
/// design-lane post-commit intent outbox that rides in the same commit.
/// </summary>
public static class GroundworkDesignAtomicWriteStorageManifest
{
    /// <summary>
    /// The frozen legacy document stamp, not a migration knob. Adding a storage unit or an index is a
    /// physicalized schema change and needs no change here; bumping it would break parsing for every kind
    /// that stamps with it.
    /// </summary>
    public const string SchemaVersion = "1.0.0";

    public const string FeatureIdentity = "elsa-design-atomic-write";
    public const string ManifestOwner = "elsa.design.atomic-write";
    public const string DesignOperationDocumentKind = "designOperation";
    public const string DesignOperationPhysicalTableName = DesignOperationDocumentKind;
    public const string AtomicWriteRouteIdentity = "design-atomic-write";
    public const string MultiDocumentTransactionsTopologyIdentity = "multi-document-transactions";

    /// <summary>
    /// The design lane's post-commit outbox. A cross-target publication becomes done when the design commit
    /// lands, but work derived from it — today the publishing receipt — belongs to another target and cannot
    /// join that transaction. An intent written inside the design commit is durable at exactly the moment the
    /// publication becomes done, so a redrive can finish the remainder without the caller retrying.
    /// <para>
    /// Separate from <see cref="DesignOperationDocumentKind"/>, which is append-only history: an intent is
    /// deleted once delivered, so it is mutable and short-lived by design.
    /// </para>
    /// </summary>
    public const string DesignPostCommitIntentDocumentKind = "designPostCommitIntent";

    public const string DesignPostCommitIntentPhysicalTableName = DesignPostCommitIntentDocumentKind;

    /// <summary>Claim ordering. Earliest claimable first, then recorded order, then id to break ties.</summary>
    public const string ByDesignPostCommitIntentClaimableIndex = "by-design-post-commit-intent-claimable";

    public const string ListClaimableDesignPostCommitIntentsQuery = "list-claimable";

    /// <summary>
    /// When the intent becomes claimable again. A claim leases the intent by pushing this forward, so an
    /// owner that dies without completing releases it by expiry rather than by anyone noticing.
    /// </summary>
    public const string DesignPostCommitIntentClaimableAtField = "claimableAt";

    public const string DesignPostCommitIntentRecordedAtField = "recordedAt";
    public const string DesignPostCommitIntentIdField = "intentId";

    public static StorageManifest Create()
    {
#pragma warning disable GW0001 // The physical declaration below is the authoritative storage contract.
        var operationUnit = new StorageUnit(
            new StorageUnitIdentity(DesignOperationDocumentKind),
            "Design operation ledger",
            StorageIntent.PortableDocument(),
            LifecyclePolicy.AppendOnly,
            IdentityPolicy.StringId(),
            TenancyPolicy.Scoped,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            [],
            [],
            PhysicalizationPolicy.Portable);
#pragma warning restore GW0001

        operationUnit = operationUnit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(
                    PhysicalTableDefinition.DedicatedDocumentTable(DesignOperationPhysicalTableName)),
                [],
                [])
        };

        return new StorageManifest(
            new StorageManifestIdentity(FeatureIdentity),
            new StorageManifestOwner(ManifestOwner),
            new StorageManifestVersion(SchemaVersion),
            [operationUnit, PostCommitIntentUnit()],
            new HashSet<string> { "optimistic-concurrency" },
            []);
    }

    /// <summary>
    /// The intent outbox unit. Mutable and optimistic rather than append-only: a redrive claims an intent by
    /// leasing it, and deletes it once delivered, so the table stays proportional to outstanding work rather
    /// than to history.
    /// </summary>
    private static StorageUnit PostCommitIntentUnit()
    {
        // Claim order is (claimableAt, recordedAt, intentId): due first, then the order they were recorded,
        // then the id so two intents recorded in the same tick still have a total order and a claim cannot
        // oscillate between them.
        var claimable = new LogicalIndexDeclaration(
            ByDesignPostCommitIntentClaimableIndex,
            [
                new IndexField(DesignPostCommitIntentClaimableAtField, IndexValueKind.DateTime),
                new IndexField(DesignPostCommitIntentRecordedAtField, IndexValueKind.DateTime),
                new IndexField(DesignPostCommitIntentIdField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);

        // Offset paging, not cursor: a claim mutates claimableAt, so the ordering key moves under a cursor.
        // The driver reads one bounded page per sweep and comes back, which is also what keeps a single
        // sweep's blast radius bounded.
        var route = new BoundedQueryDeclaration(
            ListClaimableDesignPostCommitIntentsQuery,
            claimable.Identity,
            new HashSet<PortableQueryOperation>
            {
                PortableQueryOperation.Equal,
                PortableQueryOperation.LessThanOrEqual
            },
            QuerySortSupport.Ascending,
            QueryPagingSupport.Offset,
            BoundedQueryExecutionClass.Ordinary,
            supportsTotalCount: false,
            sortFields:
            [
                new BoundedQuerySortField(DesignPostCommitIntentClaimableAtField, PhysicalSortDirection.Ascending),
                new BoundedQuerySortField(DesignPostCommitIntentRecordedAtField, PhysicalSortDirection.Ascending),
                new BoundedQuerySortField(DesignPostCommitIntentIdField, PhysicalSortDirection.Ascending)
            ],
            predicateFields:
            [
                new BoundedQueryPredicateField(
                    DesignPostCommitIntentClaimableAtField,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual })
            ]);

        var envelope = new DocumentEnvelopeDefinition();
        var definition = PhysicalTableDefinition.DedicatedDocumentTable(
            DesignPostCommitIntentPhysicalTableName,
            envelope,
            [
                new PhysicalIndexDefinition(
                    claimable.Identity,
                    [
                        // Scope first: storage_scope is the tenant column, so every claim sweep is
                        // tenant-bounded before it is time-bounded.
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(DesignPostCommitIntentClaimableAtField, 1),
                        new PhysicalIndexColumnDefinition(DesignPostCommitIntentRecordedAtField, 2),
                        new PhysicalIndexColumnDefinition(DesignPostCommitIntentIdField, 3)
                    ],
                    missingValueBehavior: MissingValueBehavior.Excluded)
            ],
            linkedProjectedColumns:
            [
                new ProjectedColumnDefinition(
                    DesignPostCommitIntentClaimableAtField,
                    DesignPostCommitIntentClaimableAtField,
                    PortablePhysicalType.DateTime,
                    IsNullable: false),
                new ProjectedColumnDefinition(
                    DesignPostCommitIntentRecordedAtField,
                    DesignPostCommitIntentRecordedAtField,
                    PortablePhysicalType.DateTime,
                    IsNullable: false),
                new ProjectedColumnDefinition(
                    DesignPostCommitIntentIdField,
                    DesignPostCommitIntentIdField,
                    PortablePhysicalType.String,
                    IsNullable: false)
            ],
            linkedProjectionLogicalName: $"{DesignPostCommitIntentPhysicalTableName}_indexes");

#pragma warning disable GW0001 // The physical declaration below is the authoritative storage contract.
        var unit = new StorageUnit(
            new StorageUnitIdentity(DesignPostCommitIntentDocumentKind),
            "Design post-commit intent outbox",
            StorageIntent.PortableDocument(),
            LifecyclePolicy.Mutable,
            IdentityPolicy.StringId(),
            TenancyPolicy.Scoped,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            [],
            [],
            PhysicalizationPolicy.Portable);
#pragma warning restore GW0001

        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(definition),
                [claimable],
                [route])
        };
    }
}

/// <summary>Contributes the shared design-operation ledger to the selected Groundwork deployment.</summary>
public sealed class GroundworkDesignAtomicWriteStorageManifestSource : IGroundworkStorageManifestSource
{
    public string FeatureIdentity => GroundworkDesignAtomicWriteStorageManifest.FeatureIdentity;

    public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
            FeatureIdentity,
            GroundworkDesignAtomicWriteStorageManifest.Create(),
            [],
            [
                new GroundworkStorageRouteRequirement(
                    new StorageUnitIdentity(
                        GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind),
                    GroundworkDesignAtomicWriteStorageManifest.AtomicWriteRouteIdentity,
                    new HashSet<CapabilityId> { WellKnownCapabilities.AtomicCommit })
            ],
            [
                new GroundworkStorageTopologyRequirement(
                    GroundworkDesignAtomicWriteStorageManifest.MultiDocumentTransactionsTopologyIdentity)
            ],
            [GroundworkDesignAtomicWriteStorageManifest.AtomicWriteRouteIdentity]));
    }
}
