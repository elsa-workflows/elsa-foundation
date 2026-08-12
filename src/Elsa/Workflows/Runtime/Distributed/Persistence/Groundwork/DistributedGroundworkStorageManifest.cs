using Elsa.Workflows.Runtime.Distributed.Contracts;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;

/// <summary>
/// Declares explicit physical storage and finite compiled routes for the durable distributed-runtime stores.
/// Mutations remain provider-atomic document operations; scale-bearing placement and transport discovery executes
/// predicates, stable ordering, latest-per-execution selection, counting, and limits at the provider seam.
/// </summary>
public static class DistributedGroundworkStorageManifest
{
    public const string SchemaVersion = "1.1.0";

    public const string PlacementsTable = "execution_placements";
    public const string CommandStreamHeadsTable = "execution_command_stream_heads";
    public const string CommandTransportTable = "execution_command_transport";

    public const string PlacementByOwnerExpiryIndex = "placement-by-owner-expiry";
    public const string CommandByExecutionSequenceIndex = "command-by-execution-sequence";
    public const string PendingCommandByExecutionSequenceIndex = "pending-command-by-execution-sequence";

    public const string WorkflowExecutionIdField = "workflowExecutionId";
    public const string WorkflowExecutionIdKeyField = "workflowExecutionIdKey";
    public const string OwnerIdField = "ownerId";
    public const string OwnerIdComparisonKeyField = "ownerIdComparisonKey";
    public const string OwnerIdLookupKeyField = "ownerIdLookupKey";
    public const string ExpiresAtField = "expiresAt";
    public const string VisibleAtField = "visibleAt";
    public const string LeaseOwnerIdField = "leaseOwnerId";
    public const string LeaseTokenField = "leaseToken";
    public const string SequenceField = "sequence";
    public const string LastSequenceField = "lastSequence";
    public const string CollectionField = "collection";

    public const string ListOwnedPlacementsQuery = "list-owned-live-placements";
    public const string LeaseVisibleCommandsQuery = "lease-visible-commands-by-execution";
    public const string ListPendingExecutionIdsQuery = "list-visible-command-executions";
    public const string CountPendingCommandsQuery = "count-pending-commands-by-execution";

    public static StorageManifest Create() => new(
        new StorageManifestIdentity("elsa-workflows-runtime-distributed"),
        new StorageManifestOwner("elsa.workflows.runtime.distributed"),
        new StorageManifestVersion(SchemaVersion),
        [
            PlacementUnit(),
            CommandStreamHeadUnit(),
            CommandTransportUnit()
        ],
        new HashSet<string> { "schema-history", "optimistic-concurrency" },
        []);

    private static StorageUnit PlacementUnit()
    {
        var envelope = new DocumentEnvelopeDefinition();
        var logicalIndex = new LogicalIndexDeclaration(
            PlacementByOwnerExpiryIndex,
            [
                new IndexField(OwnerIdLookupKeyField),
                new IndexField(ExpiresAtField, IndexValueKind.DateTime),
                new IndexField(WorkflowExecutionIdKeyField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var table = PhysicalTableDefinition.PhysicalEntityTable(
            PlacementsTable,
            [
                OrdinalKeyProjection(WorkflowExecutionIdKeyField),
                OrdinalKeyProjection(OwnerIdComparisonKeyField),
                StringProjection(OwnerIdLookupKeyField, length: 64),
                new ProjectedColumnDefinition(
                    ExpiresAtField,
                    ExpiresAtField,
                    PortablePhysicalType.DateTime,
                    IsNullable: false)
            ],
            envelope,
            [
                new PhysicalIndexDefinition(
                    logicalIndex.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(OwnerIdLookupKeyField, 1),
                        new PhysicalIndexColumnDefinition(ExpiresAtField, 2),
                        new PhysicalIndexColumnDefinition(WorkflowExecutionIdKeyField, 3)
                    ],
                    missingValueBehavior: logicalIndex.MissingValueBehavior)
            ]);
        var query = new BoundedQueryDeclaration(
            ListOwnedPlacementsQuery,
            logicalIndex.Identity,
            new HashSet<PortableQueryOperation>
            {
                PortableQueryOperation.Equal,
                PortableQueryOperation.GreaterThan
            },
            QuerySortSupport.Ascending,
            QueryPagingSupport.None,
            BoundedQueryExecutionClass.ScaleBearing,
            sortFields:
            [
                new BoundedQuerySortField(OwnerIdLookupKeyField, PhysicalSortDirection.Ascending),
                new BoundedQuerySortField(ExpiresAtField, PhysicalSortDirection.Ascending),
                new BoundedQuerySortField(WorkflowExecutionIdKeyField, PhysicalSortDirection.Ascending)
            ],
            predicateFields:
            [
                new BoundedQueryPredicateField(
                    OwnerIdLookupKeyField,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal }),
                new BoundedQueryPredicateField(
                    ExpiresAtField,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.GreaterThan })
            ],
            residualPredicateFields:
            [
                new BoundedQueryResidualPredicateField(
                    OwnerIdComparisonKeyField,
                    IndexValueKind.String,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
            ]);

        return Unit(
            DistributedRuntimeStorageManifest.ExecutionPlacementDocumentKind,
            "Execution placement lease",
            table,
            [logicalIndex],
            [query]);
    }

    private static StorageUnit CommandTransportUnit()
    {
        var envelope = new DocumentEnvelopeDefinition();
        var logicalIndex = new LogicalIndexDeclaration(
            CommandByExecutionSequenceIndex,
            [
                new IndexField(WorkflowExecutionIdKeyField),
                new IndexField(SequenceField, IndexValueKind.Number)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var pendingIndex = new LogicalIndexDeclaration(
            PendingCommandByExecutionSequenceIndex,
            [
                new IndexField(CollectionField),
                new IndexField(WorkflowExecutionIdKeyField),
                new IndexField(SequenceField, IndexValueKind.Number)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var table = PhysicalTableDefinition.PhysicalEntityTable(
            CommandTransportTable,
            [
                StringProjection(CollectionField, length: 128),
                OrdinalKeyProjection(WorkflowExecutionIdKeyField),
                new ProjectedColumnDefinition(
                    VisibleAtField,
                    VisibleAtField,
                    PortablePhysicalType.DateTime,
                    IsNullable: false),
                StringProjection(LeaseOwnerIdField, length: 64),
                new ProjectedColumnDefinition(
                    LeaseTokenField,
                    LeaseTokenField,
                    PortablePhysicalType.Int64,
                    IsNullable: false),
                new ProjectedColumnDefinition(
                    SequenceField,
                    SequenceField,
                    PortablePhysicalType.Int64,
                    IsNullable: false)
            ],
            envelope,
            [
                new PhysicalIndexDefinition(
                    logicalIndex.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(WorkflowExecutionIdKeyField, 1),
                        new PhysicalIndexColumnDefinition(SequenceField, 2)
                    ],
                    missingValueBehavior: logicalIndex.MissingValueBehavior),
                new PhysicalIndexDefinition(
                    pendingIndex.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(CollectionField, 1),
                        new PhysicalIndexColumnDefinition(WorkflowExecutionIdKeyField, 2),
                        new PhysicalIndexColumnDefinition(
                            SequenceField,
                            3,
                            PhysicalSortDirection.Descending)
                    ],
                    missingValueBehavior: pendingIndex.MissingValueBehavior)
            ]);
        var lease = new BoundedQueryDeclaration(
            LeaseVisibleCommandsQuery,
            logicalIndex.Identity,
            new HashSet<PortableQueryOperation>
            {
                PortableQueryOperation.Equal,
                PortableQueryOperation.LessThanOrEqual
            },
            QuerySortSupport.Ascending,
            QueryPagingSupport.None,
            BoundedQueryExecutionClass.ScaleBearing,
            sortFields:
            [
                new BoundedQuerySortField(SequenceField, PhysicalSortDirection.Ascending)
            ],
            predicateFields:
            [
                new BoundedQueryPredicateField(
                    WorkflowExecutionIdKeyField,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
            ],
            residualPredicateFields:
            [
                new BoundedQueryResidualPredicateField(
                    VisibleAtField,
                    IndexValueKind.DateTime,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual })
            ]);
        var pendingExecutions = new BoundedQueryDeclaration(
            ListPendingExecutionIdsQuery,
            pendingIndex.Identity,
            new HashSet<PortableQueryOperation>
            {
                PortableQueryOperation.Equal,
                PortableQueryOperation.LessThanOrEqual
            },
            QuerySortSupport.Both,
            QueryPagingSupport.None,
            BoundedQueryExecutionClass.ScaleBearing,
            sortFields:
            [
                new BoundedQuerySortField(WorkflowExecutionIdKeyField, PhysicalSortDirection.Ascending),
                new BoundedQuerySortField(SequenceField, PhysicalSortDirection.Descending)
            ],
            predicateFields:
            [
                new BoundedQueryPredicateField(
                    CollectionField,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
            ],
            residualPredicateFields:
            [
                new BoundedQueryResidualPredicateField(
                    VisibleAtField,
                    IndexValueKind.DateTime,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual })
            ],
            latestPerKeyPath: WorkflowExecutionIdKeyField);
        var count = new BoundedQueryDeclaration(
            CountPendingCommandsQuery,
            logicalIndex.Identity,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
            QuerySortSupport.None,
            QueryPagingSupport.None,
            BoundedQueryExecutionClass.ScaleBearing,
            supportsTotalCount: true,
            predicateFields:
            [
                new BoundedQueryPredicateField(
                    WorkflowExecutionIdKeyField,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
            ],
            resultOperations: new HashSet<BoundedQueryResultOperation>
            {
                BoundedQueryResultOperation.Count
            });

        return Unit(
            DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind,
            "Execution command transport item",
            table,
            [logicalIndex, pendingIndex],
            [lease, pendingExecutions, count]);
    }

    private static StorageUnit CommandStreamHeadUnit()
    {
        var envelope = new DocumentEnvelopeDefinition();
        var table = PhysicalTableDefinition.PhysicalEntityTable(
            CommandStreamHeadsTable,
            [
                OrdinalKeyProjection(WorkflowExecutionIdKeyField),
                new ProjectedColumnDefinition(
                    LastSequenceField,
                    LastSequenceField,
                    PortablePhysicalType.Int64,
                    IsNullable: false)
            ],
            envelope,
            []);

        return Unit(
            DistributedRuntimeStorageManifest.ExecutionCommandStreamHeadDocumentKind,
            "Execution command stream head",
            table,
            [],
            []);
    }

    // Groundwork's required positional StorageUnit constructor still carries the legacy physicalization argument;
    // PhysicalStorage below is authoritative.
#pragma warning disable GW0001
    private static StorageUnit Unit(
        string documentKind,
        string label,
        PhysicalTableDefinition table,
        IReadOnlyList<LogicalIndexDeclaration> indexes,
        IReadOnlyList<BoundedQueryDeclaration> queries) =>
        new(
            new StorageUnitIdentity(documentKind),
            label,
            StorageIntent.PortableDocument(),
            LifecyclePolicy.Mutable,
            IdentityPolicy.StringId(),
            TenancyPolicy.Scoped,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            [],
            [],
            PhysicalizationPolicy.Portable)
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(table),
                indexes,
                queries)
        };
#pragma warning restore GW0001

    private static ProjectedColumnDefinition OrdinalKeyProjection(string path) =>
        StringProjection(
            path,
            checked(DistributedRuntimeIdentityConstraints.MaximumLength * 4));

    private static ProjectedColumnDefinition StringProjection(string path, int length) =>
        new(path, path, PortablePhysicalType.String, Length: length, IsNullable: false);
}
