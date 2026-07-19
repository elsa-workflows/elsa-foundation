using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Persistence.Groundwork;

/// <summary>
/// Gives due timer and recurring-schedule reads a provider-executable predicate, stable order, and finite
/// result window. The logical identifier is the final order component so applying <c>take</c> at the storage
/// boundary remains equivalent when multiple records share the same deadline.
/// </summary>
internal static class DueWorkStoragePhysicalizer
{
    public static StorageManifest AddRoutes(StorageManifest manifest) => manifest with
    {
        StorageUnits = manifest.StorageUnits.Select(unit => unit.Identity.Value switch
        {
            ElsaRuntimeStorageManifest.DurableTimerDocumentKind => AddTimerRoute(unit),
            ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind => AddScheduleRoute(unit),
            _ => unit
        }).ToArray()
    };

    private static StorageUnit AddTimerRoute(StorageUnit unit) =>
        AddRoute(
            unit,
            ElsaRuntimeStorageManifest.DurableTimerByDueTimeAndTimerId,
            [
                new IndexField(
                    ElsaRuntimeStorageManifest.DurableTimerDueTimeField,
                    IndexValueKind.DateTime),
                new IndexField(ElsaRuntimeStorageManifest.DurableTimerIdField)
            ],
            [
                ElsaRuntimeStorageManifest.DurableTimerByDueTime,
                ElsaRuntimeStorageManifest.ByTimerIdIndex
            ],
            ElsaRuntimeStorageManifest.ListDueDurableTimersQuery,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual },
            [
                new BoundedQueryPredicateField(
                    ElsaRuntimeStorageManifest.DurableTimerDueTimeField,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual })
            ],
            [
                new BoundedQuerySortField(
                    ElsaRuntimeStorageManifest.DurableTimerDueTimeField,
                    PhysicalSortDirection.Ascending),
                new BoundedQuerySortField(
                    ElsaRuntimeStorageManifest.DurableTimerIdField,
                    PhysicalSortDirection.Ascending)
            ]);

    private static StorageUnit AddScheduleRoute(StorageUnit unit) =>
        AddRoute(
            unit,
            ElsaRuntimeStorageManifest.RecurringTriggerScheduleByActiveNextOccurrenceAndScheduleId,
            [
                new IndexField(
                    ElsaRuntimeStorageManifest.RecurringTriggerScheduleIsActiveField,
                    IndexValueKind.Boolean),
                new IndexField(
                    ElsaRuntimeStorageManifest.RecurringTriggerScheduleNextOccurrenceField,
                    IndexValueKind.DateTime),
                new IndexField(ElsaRuntimeStorageManifest.RecurringTriggerScheduleIdField)
            ],
            [
                ElsaRuntimeStorageManifest.ByRecurringScheduleActiveIndex,
                ElsaRuntimeStorageManifest.RecurringTriggerScheduleByNextOccurrence,
                ElsaRuntimeStorageManifest.ByRecurringScheduleIdIndex
            ],
            ElsaRuntimeStorageManifest.ListDueRecurringTriggerSchedulesQuery,
            new HashSet<PortableQueryOperation>
            {
                PortableQueryOperation.Equal,
                PortableQueryOperation.LessThanOrEqual
            },
            [
                new BoundedQueryPredicateField(
                    ElsaRuntimeStorageManifest.RecurringTriggerScheduleIsActiveField,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal }),
                new BoundedQueryPredicateField(
                    ElsaRuntimeStorageManifest.RecurringTriggerScheduleNextOccurrenceField,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual })
            ],
            [
                new BoundedQuerySortField(
                    ElsaRuntimeStorageManifest.RecurringTriggerScheduleIsActiveField,
                    PhysicalSortDirection.Ascending),
                new BoundedQuerySortField(
                    ElsaRuntimeStorageManifest.RecurringTriggerScheduleNextOccurrenceField,
                    PhysicalSortDirection.Ascending),
                new BoundedQuerySortField(
                    ElsaRuntimeStorageManifest.RecurringTriggerScheduleIdField,
                    PhysicalSortDirection.Ascending)
            ]);

    private static StorageUnit AddRoute(
        StorageUnit unit,
        string indexIdentity,
        IndexField[] fields,
        string[] projectedColumns,
        string queryIdentity,
        IReadOnlySet<PortableQueryOperation> operations,
        BoundedQueryPredicateField[] predicates,
        BoundedQuerySortField[] order)
    {
        if (unit.PhysicalStorage is not { } storage ||
            storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy { Definition: var definition } ||
            definition.Form != PhysicalStorageForm.SharedDocuments)
        {
            throw new InvalidOperationException(
                $"Runtime due-work storage unit '{unit.Identity.Value}' requires explicit shared-document physicalization.");
        }

        var logicalIndex = new LogicalIndexDeclaration(
            indexIdentity,
            fields,
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var physicalIndex = new PhysicalIndexDefinition(
            logicalIndex.Identity,
            [
                new PhysicalIndexColumnDefinition(new DocumentEnvelopeDefinition().StorageScopeColumn, 0),
                .. projectedColumns.Select((column, index) =>
                    new PhysicalIndexColumnDefinition(column, index + 1))
            ]);
        var table = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            definition.ProjectedColumns,
            definition.Indexes.Concat([physicalIndex]).ToArray(),
            definition.SchemaVersion,
            definition.Evolution,
            definition.LinkedProjectionLogicalName,
            definition.LinkedKey);
        var query = new BoundedQueryDeclaration(
            queryIdentity,
            logicalIndex.Identity,
            operations,
            QuerySortSupport.Ascending,
            QueryPagingSupport.None,
            BoundedQueryExecutionClass.ScaleBearing,
            sortFields: order,
            predicateFields: predicates);

        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                storage.ProvisioningMode,
                PhysicalStoragePolicy.Explicit(table),
                storage.LogicalIndexes.Concat([logicalIndex]).ToArray(),
                storage.BoundedQueries
                    .Where(existing => !StringComparer.Ordinal.Equals(existing.Identity, queryIdentity))
                    .Append(query)
                    .ToArray(),
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }
}
