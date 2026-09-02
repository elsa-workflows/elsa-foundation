using Elsa.Workflows.Runtime.Distributed.Contracts;
using Groundwork.Kernel;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;

/// <summary>Fresh Groundwork v2 declarations for the distributed runtime stores.</summary>
/// <remarks>
/// The distributed family deliberately owns ordinary v2 rows rather than a v1 document
/// manifest. Scoping and optimistic concurrency are part of each unit declaration, while
/// bounded reads are expressed by the public query model at the store boundary.
/// </remarks>
public static class DistributedGroundworkStorageManifest
{
    public const string PlacementUnitId = "elsa-distributed-execution-placement";
    public const string PlacementUnitName = "elsa_distributed_execution_placement";
    public const string CommandTransportUnitId = "elsa-distributed-command-transport";
    public const string CommandTransportUnitName = "elsa_distributed_command_transport";
    public const string CommandStreamHeadUnitId = "elsa-distributed-command-stream-head";
    public const string CommandStreamHeadUnitName = "elsa_distributed_command_stream_head";

    public const string PlacementByOwnerExpiryIndex = "elsa_distributed_placement_owner_expiry";
    public const string CommandByExecutionSequenceIndex = "elsa_distributed_command_execution_sequence";
    public const string CommandByExecutionVisibilityIndex = "elsa_distributed_command_execution_visibility";
    public const string PendingCommandByExecutionSequenceIndex = "elsa_distributed_command_pending_execution_sequence";
    public const string PendingCommandHeadByExecutionIndex = "elsa_distributed_command_pending_head_execution";
    // transport:{escaped execution id}:{Int64 sequence}; every UTF-16 code unit may expand to %XX.
    public const int TransportItemIdMaximumLength = 10 + (DistributedRuntimeIdentityConstraints.MaximumLength * 3) + 1 + 19;

    public const string WorkflowExecutionIdField = "workflowExecutionId";
    public const string TransportItemIdField = "transportItemId";
    public const string OwnerIdField = "ownerId";
    public const string PlacementTokenField = "placementToken";
    public const string AcquiredAtField = "acquiredAt";
    public const string ExpiresAtField = "expiresAt";
    public const string VisibleAtField = "visibleAt";
    public const string EnqueuedAtField = "enqueuedAt";
    public const string LeaseOwnerIdField = "leaseOwnerId";
    public const string LeaseTokenField = "leaseToken";
    public const string SequenceField = "sequence";
    public const string LastSequenceField = "lastSequence";
    public const string PendingCountField = "pendingCount";
    public const string PendingVisibleAtField = "pendingVisibleAt";
    public const string PendingSequenceField = "pendingSequence";
    public const string PayloadField = "payload";

    // Stable identities retained for diagnostics and query evidence. v2 queries carry their
    // actual shape in QueryRequest; they are not v1 provider route declarations.
    public const string ListOwnedPlacementsQuery = "list-owned-live-placements";
    public const string LeaseVisibleCommandsQuery = "lease-visible-commands-by-execution";
    public const string ListPendingExecutionIdsQuery = "list-visible-command-executions";
    public const string CountPendingCommandsQuery = "count-pending-commands-by-execution";

    public static IReadOnlyList<StorageUnit> CreateUnits() =>
    [
        CreatePlacementUnit(),
        CreateCommandStreamHeadUnit(),
        CreateCommandTransportUnit()
    ];

    public static StorageUnit CreatePlacementUnit() =>
        StorageUnit.Declare(PlacementUnitId, PlacementUnitName)
            .String(WorkflowExecutionIdField, DistributedRuntimeIdentityConstraints.MaximumLength, column => column.Required())
            .String(OwnerIdField, DistributedRuntimeIdentityConstraints.MaximumLength, column => column.Required())
            .Int64(PlacementTokenField, column => column.Required())
            .Timestamp(AcquiredAtField, column => column.Required())
            .Timestamp(ExpiresAtField, column => column.Required())
            .Json(PayloadField, column => column.Required())
            .Key(WorkflowExecutionIdField)
            .Index(PlacementByOwnerExpiryIndex, OwnerIdField, ExpiresAtField, WorkflowExecutionIdField)
            .OptimisticConcurrency()
            .Scoped()
            .Build();

    public static StorageUnit CreateCommandStreamHeadUnit() =>
        StorageUnit.Declare(CommandStreamHeadUnitId, CommandStreamHeadUnitName)
            .String(WorkflowExecutionIdField, DistributedRuntimeIdentityConstraints.MaximumLength, column => column.Required())
            .Int64(LastSequenceField, column => column.Required())
            .Int64(PendingCountField, column => column.Required())
            .Timestamp(PendingVisibleAtField, column => column.Required())
            .Int64(PendingSequenceField, column => column.Required())
            .Json(PayloadField, column => column.Required())
            .Key(WorkflowExecutionIdField)
            .Index(PendingCommandHeadByExecutionIndex, index => index
                .Ascending(WorkflowExecutionIdField)
                .Ascending(PendingVisibleAtField))
            .OptimisticConcurrency()
            .Scoped()
            .Build();

    public static StorageUnit CreateCommandTransportUnit() =>
        StorageUnit.Declare(CommandTransportUnitId, CommandTransportUnitName)
            .String(TransportItemIdField, TransportItemIdMaximumLength, column => column.Required())
            .String(WorkflowExecutionIdField, DistributedRuntimeIdentityConstraints.MaximumLength, column => column.Required())
            .Int64(SequenceField, column => column.Required())
            .Timestamp(EnqueuedAtField, column => column.Required())
            .Timestamp(VisibleAtField, column => column.Required())
            .String(LeaseOwnerIdField, DistributedRuntimeIdentityConstraints.MaximumLength)
            .Int64(LeaseTokenField, column => column.Required())
            .Json(PayloadField, column => column.Required())
            .Key(TransportItemIdField)
            .Index(CommandByExecutionSequenceIndex, WorkflowExecutionIdField, SequenceField)
            .Index(CommandByExecutionVisibilityIndex, WorkflowExecutionIdField, VisibleAtField, SequenceField)
            .Index(PendingCommandByExecutionSequenceIndex, index => index
                .Ascending(WorkflowExecutionIdField)
                .Descending(SequenceField))
            .OptimisticConcurrency()
            .Scoped()
            .Build();
}
