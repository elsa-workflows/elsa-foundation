using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Persistence.Groundwork;

internal static class ExecutionLivenessRecoveryStoragePhysicalizer
{
    private const string HasOperationalOwnerColumn = "recovery_has_operational_owner";
    private const string InterruptionStatusColumn = "recovery_interruption_status";
    private const string InterruptedAtColumn = "recovery_interrupted_at";
    private const string LeaseOwnerColumn = "recovery_lease_owner";
    private const string LeaseAcquiredAtColumn = "recovery_lease_acquired_at";
    private const string LeaseExpiresAtColumn = "recovery_lease_expires_at";
    private const string HeartbeatOwnerColumn = "recovery_heartbeat_owner";
    private const string HeartbeatRecordedAtColumn = "recovery_heartbeat_recorded_at";

    private static readonly HashSet<string> RecoveryIndexIdentities =
    [
        ElsaRuntimeStorageManifest.RecoveryDetectedIndex,
        ElsaRuntimeStorageManifest.RecoveryDetectedLeaseOwnerIndex,
        ElsaRuntimeStorageManifest.RecoveryDetectedHeartbeatOwnerIndex,
        ElsaRuntimeStorageManifest.RecoveryDetectedOwnerlessIndex,
        ElsaRuntimeStorageManifest.RecoveryLeaseExpiryIndex,
        ElsaRuntimeStorageManifest.RecoveryLeaseExpiryOwnerIndex,
        ElsaRuntimeStorageManifest.RecoveryLeaseAcquisitionIndex,
        ElsaRuntimeStorageManifest.RecoveryLeaseAcquisitionOwnerIndex,
        ElsaRuntimeStorageManifest.RecoveryHeartbeatIndex,
        ElsaRuntimeStorageManifest.RecoveryHeartbeatOwnerIndex
    ];

    private static readonly HashSet<string> RecoveryFieldPaths =
    [
        ElsaRuntimeStorageManifest.RecoveryHasOperationalOwnerField,
        ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField,
        ElsaRuntimeStorageManifest.RecoveryInterruptedAtField,
        ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField,
        ElsaRuntimeStorageManifest.RecoveryLeaseAcquiredAtField,
        ElsaRuntimeStorageManifest.RecoveryLeaseExpiresAtField,
        ElsaRuntimeStorageManifest.RecoveryHeartbeatOwnerIdField,
        ElsaRuntimeStorageManifest.RecoveryHeartbeatRecordedAtField
    ];

    private static readonly HashSet<string> RecoveryQueryIdentities =
    [
        ElsaRuntimeStorageManifest.ListRecoveryDetectedQuery,
        ElsaRuntimeStorageManifest.ListRecoveryDetectedByLeaseOwnerQuery,
        ElsaRuntimeStorageManifest.ListRecoveryDetectedByHeartbeatOwnerQuery,
        ElsaRuntimeStorageManifest.ListRecoveryDetectedOwnerlessQuery,
        ElsaRuntimeStorageManifest.ListRecoveryByLeaseExpiryQuery,
        ElsaRuntimeStorageManifest.ListRecoveryByLeaseExpiryAndOwnerQuery,
        ElsaRuntimeStorageManifest.ListRecoveryByLeaseAcquisitionQuery,
        ElsaRuntimeStorageManifest.ListRecoveryByLeaseAcquisitionAndOwnerQuery,
        ElsaRuntimeStorageManifest.ListRecoveryByHeartbeatQuery,
        ElsaRuntimeStorageManifest.ListRecoveryByHeartbeatAndOwnerQuery
    ];

    public static StorageManifest AddRoutes(StorageManifest manifest) => manifest with
    {
        StorageUnits = manifest.StorageUnits.Select(unit =>
            StringComparer.Ordinal.Equals(
                unit.Identity.Value,
                ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind)
                ? AddRoutes(unit)
                : unit).ToArray()
    };

    private static StorageUnit AddRoutes(StorageUnit unit)
    {
        if (unit.PhysicalStorage is not { } storage ||
            storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy { Definition: var definition } ||
            definition.Form != PhysicalStorageForm.SharedDocuments)
        {
            throw new InvalidOperationException(
                "The execution-liveness storage unit requires an explicit shared-document physicalization.");
        }

        var recoveryIndexes = new[]
        {
            Logical(
                ElsaRuntimeStorageManifest.RecoveryDetectedIndex,
                Number(ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField),
                DateTime(ElsaRuntimeStorageManifest.RecoveryInterruptedAtField)),
            Logical(
                ElsaRuntimeStorageManifest.RecoveryDetectedLeaseOwnerIndex,
                Number(ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField),
                Keyword(ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField),
                DateTime(ElsaRuntimeStorageManifest.RecoveryInterruptedAtField)),
            Logical(
                ElsaRuntimeStorageManifest.RecoveryDetectedHeartbeatOwnerIndex,
                Number(ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField),
                Keyword(ElsaRuntimeStorageManifest.RecoveryHeartbeatOwnerIdField),
                DateTime(ElsaRuntimeStorageManifest.RecoveryInterruptedAtField)),
            Logical(
                ElsaRuntimeStorageManifest.RecoveryDetectedOwnerlessIndex,
                Number(ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField),
                Boolean(ElsaRuntimeStorageManifest.RecoveryHasOperationalOwnerField),
                DateTime(ElsaRuntimeStorageManifest.RecoveryInterruptedAtField)),
            Logical(
                ElsaRuntimeStorageManifest.RecoveryLeaseExpiryIndex,
                DateTime(ElsaRuntimeStorageManifest.RecoveryLeaseExpiresAtField)),
            Logical(
                ElsaRuntimeStorageManifest.RecoveryLeaseExpiryOwnerIndex,
                Keyword(ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField),
                DateTime(ElsaRuntimeStorageManifest.RecoveryLeaseExpiresAtField)),
            Logical(
                ElsaRuntimeStorageManifest.RecoveryLeaseAcquisitionIndex,
                DateTime(ElsaRuntimeStorageManifest.RecoveryLeaseAcquiredAtField)),
            Logical(
                ElsaRuntimeStorageManifest.RecoveryLeaseAcquisitionOwnerIndex,
                Keyword(ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField),
                DateTime(ElsaRuntimeStorageManifest.RecoveryLeaseAcquiredAtField)),
            Logical(
                ElsaRuntimeStorageManifest.RecoveryHeartbeatIndex,
                DateTime(ElsaRuntimeStorageManifest.RecoveryHeartbeatRecordedAtField)),
            Logical(
                ElsaRuntimeStorageManifest.RecoveryHeartbeatOwnerIndex,
                Keyword(ElsaRuntimeStorageManifest.RecoveryHeartbeatOwnerIdField),
                DateTime(ElsaRuntimeStorageManifest.RecoveryHeartbeatRecordedAtField))
        };
        var indexes = recoveryIndexes.ToDictionary(index => index.Identity, StringComparer.Ordinal);
        var projectedColumns = new[]
        {
            Projected(HasOperationalOwnerColumn, ElsaRuntimeStorageManifest.RecoveryHasOperationalOwnerField, PortablePhysicalType.Boolean),
            Projected(InterruptionStatusColumn, ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField, PortablePhysicalType.Int32),
            Projected(InterruptedAtColumn, ElsaRuntimeStorageManifest.RecoveryInterruptedAtField, PortablePhysicalType.DateTime),
            Projected(LeaseOwnerColumn, ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField, PortablePhysicalType.String, 450),
            Projected(LeaseAcquiredAtColumn, ElsaRuntimeStorageManifest.RecoveryLeaseAcquiredAtField, PortablePhysicalType.DateTime),
            Projected(LeaseExpiresAtColumn, ElsaRuntimeStorageManifest.RecoveryLeaseExpiresAtField, PortablePhysicalType.DateTime),
            Projected(HeartbeatOwnerColumn, ElsaRuntimeStorageManifest.RecoveryHeartbeatOwnerIdField, PortablePhysicalType.String, 450),
            Projected(HeartbeatRecordedAtColumn, ElsaRuntimeStorageManifest.RecoveryHeartbeatRecordedAtField, PortablePhysicalType.DateTime)
        };
        var retainedColumns = definition.ProjectedColumns
            .Where(column => !RecoveryFieldPaths.Contains(column.Path))
            .ToArray();
        var projectedByPath = retainedColumns
            .Concat(projectedColumns)
            .ToDictionary(column => column.Path, column => column.LogicalName, StringComparer.Ordinal);
        var augmentedDefinition = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            retainedColumns.Concat(projectedColumns).ToArray(),
            definition.Indexes
                .Where(index => !RecoveryIndexIdentities.Contains(index.LogicalName))
                .Concat(recoveryIndexes.Select(index => Physical(index, projectedByPath)))
                .ToArray(),
            definition.SchemaVersion,
            definition.Evolution,
            definition.LinkedProjectionLogicalName,
            definition.LinkedKey);
        var routes = new[]
        {
            Route(
                ElsaRuntimeStorageManifest.ListRecoveryDetectedQuery,
                RequiredIndex(indexes, ElsaRuntimeStorageManifest.RecoveryDetectedIndex),
                Equal(ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField)),
            Route(
                ElsaRuntimeStorageManifest.ListRecoveryDetectedByLeaseOwnerQuery,
                RequiredIndex(indexes, ElsaRuntimeStorageManifest.RecoveryDetectedLeaseOwnerIndex),
                Equal(ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField),
                Equal(ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField)),
            Route(
                ElsaRuntimeStorageManifest.ListRecoveryDetectedByHeartbeatOwnerQuery,
                RequiredIndex(indexes, ElsaRuntimeStorageManifest.RecoveryDetectedHeartbeatOwnerIndex),
                Equal(ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField),
                Equal(ElsaRuntimeStorageManifest.RecoveryHeartbeatOwnerIdField)),
            Route(
                ElsaRuntimeStorageManifest.ListRecoveryDetectedOwnerlessQuery,
                RequiredIndex(indexes, ElsaRuntimeStorageManifest.RecoveryDetectedOwnerlessIndex),
                Equal(ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField),
                Equal(ElsaRuntimeStorageManifest.RecoveryHasOperationalOwnerField)),
            Route(
                ElsaRuntimeStorageManifest.ListRecoveryByLeaseExpiryQuery,
                RequiredIndex(indexes, ElsaRuntimeStorageManifest.RecoveryLeaseExpiryIndex),
                Due(ElsaRuntimeStorageManifest.RecoveryLeaseExpiresAtField)),
            Route(
                ElsaRuntimeStorageManifest.ListRecoveryByLeaseExpiryAndOwnerQuery,
                RequiredIndex(indexes, ElsaRuntimeStorageManifest.RecoveryLeaseExpiryOwnerIndex),
                Equal(ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField),
                Due(ElsaRuntimeStorageManifest.RecoveryLeaseExpiresAtField)),
            Route(
                ElsaRuntimeStorageManifest.ListRecoveryByLeaseAcquisitionQuery,
                RequiredIndex(indexes, ElsaRuntimeStorageManifest.RecoveryLeaseAcquisitionIndex),
                Due(ElsaRuntimeStorageManifest.RecoveryLeaseAcquiredAtField)),
            Route(
                ElsaRuntimeStorageManifest.ListRecoveryByLeaseAcquisitionAndOwnerQuery,
                RequiredIndex(indexes, ElsaRuntimeStorageManifest.RecoveryLeaseAcquisitionOwnerIndex),
                Equal(ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField),
                Due(ElsaRuntimeStorageManifest.RecoveryLeaseAcquiredAtField)),
            Route(
                ElsaRuntimeStorageManifest.ListRecoveryByHeartbeatQuery,
                RequiredIndex(indexes, ElsaRuntimeStorageManifest.RecoveryHeartbeatIndex),
                Due(ElsaRuntimeStorageManifest.RecoveryHeartbeatRecordedAtField)),
            Route(
                ElsaRuntimeStorageManifest.ListRecoveryByHeartbeatAndOwnerQuery,
                RequiredIndex(indexes, ElsaRuntimeStorageManifest.RecoveryHeartbeatOwnerIndex),
                Equal(ElsaRuntimeStorageManifest.RecoveryHeartbeatOwnerIdField),
                Due(ElsaRuntimeStorageManifest.RecoveryHeartbeatRecordedAtField))
        };

        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                storage.ProvisioningMode,
                PhysicalStoragePolicy.Explicit(augmentedDefinition),
                storage.LogicalIndexes
                    .Where(index => !RecoveryIndexIdentities.Contains(index.Identity))
                    .Concat(recoveryIndexes)
                    .ToArray(),
                storage.BoundedQueries
                    .Where(query => !RecoveryQueryIdentities.Contains(query.Identity))
                    .Concat(routes)
                    .ToArray(),
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }

    private static ProjectedColumnDefinition Projected(
        string logicalName,
        string path,
        PortablePhysicalType type,
        int? length = null) =>
        new(logicalName, path, type, Length: length);

    private static PhysicalIndexDefinition Physical(
        LogicalIndexDeclaration index,
        IReadOnlyDictionary<string, string> projectedByPath) =>
        new(
            index.Identity,
            [
                new PhysicalIndexColumnDefinition(new DocumentEnvelopeDefinition().StorageScopeColumn, 0),
                ..index.Fields.Select((field, position) =>
                    new PhysicalIndexColumnDefinition(projectedByPath[field.Path], position + 1))
            ]);

    private static LogicalIndexDeclaration Logical(string identity, params IndexField[] fields) =>
        new(identity, fields, IndexValueKind.Keyword, false);

    private static IndexField Keyword(string path) => new(path, IndexValueKind.Keyword);

    private static IndexField Boolean(string path) => new(path, IndexValueKind.Boolean);

    private static IndexField Number(string path) => new(path, IndexValueKind.Number);

    private static IndexField DateTime(string path) => new(path, IndexValueKind.DateTime);

    private static LogicalIndexDeclaration RequiredIndex(
        IReadOnlyDictionary<string, LogicalIndexDeclaration> indexes,
        string identity) =>
        indexes.TryGetValue(identity, out var index)
            ? index
            : throw new InvalidOperationException(
                $"The execution-liveness storage unit does not declare required index '{identity}'.");

    private static BoundedQueryDeclaration Route(
        string identity,
        LogicalIndexDeclaration index,
        params BoundedQueryPredicateField[] predicates) =>
        new(
            identity,
            index.Identity,
            predicates.SelectMany(predicate => predicate.Operations).ToHashSet(),
            QuerySortSupport.Ascending,
            QueryPagingSupport.None,
            BoundedQueryExecutionClass.ScaleBearing,
            sortFields: index.Fields
                .Select(field => new BoundedQuerySortField(field.Path, PhysicalSortDirection.Ascending))
                .ToArray(),
            predicateFields: predicates);

    private static BoundedQueryPredicateField Equal(string path) =>
        new(path, new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal });

    private static BoundedQueryPredicateField Due(string path) =>
        new(path, new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual });
}
