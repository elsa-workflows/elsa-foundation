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
    private const string DurableTimerClaimOrderKeyColumn = "durable_timer_claim_order_key";

    public static StorageManifest AddRoutes(StorageManifest manifest) => manifest with
    {
        StorageUnits = manifest.StorageUnits.Select(unit => unit.Identity.Value switch
        {
            ElsaRuntimeStorageManifest.DurableTimerDocumentKind => AddTimerClaimRoute(AddTimerPageRoute(AddTimerRoute(unit))),
            ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind => AddSchedulePageRoutes(AddScheduleRoute(unit)),
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
            [ElsaRuntimeStorageManifest.DurableTimerByDueTime],
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

    private static StorageUnit AddTimerClaimRoute(StorageUnit unit)
    {
        if (unit.PhysicalStorage is not { } storage ||
            storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy { Definition: var definition } ||
            definition.Form != PhysicalStorageForm.SharedDocuments)
        {
            throw new InvalidOperationException(
                $"Runtime due-work storage unit '{unit.Identity.Value}' requires explicit shared-document physicalization.");
        }

        var projected = definition.ProjectedColumns
            .Where(column => column.Path != ElsaRuntimeStorageManifest.DurableTimerClaimOrderKeyField)
            .Append(new ProjectedColumnDefinition(
                DurableTimerClaimOrderKeyColumn,
                ElsaRuntimeStorageManifest.DurableTimerClaimOrderKeyField,
                PortablePhysicalType.String,
                Length: ElsaRuntimeStorageManifest.DurableTimerClaimOrderKeyProjectionLength))
            .ToArray();
        var envelope = new DocumentEnvelopeDefinition();
        var physical = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            projected,
            definition.Indexes
                .Where(index => index.LogicalName != ElsaRuntimeStorageManifest.DurableTimerByClaimOrder)
                .Append(new PhysicalIndexDefinition(
                    ElsaRuntimeStorageManifest.DurableTimerByClaimOrder,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(
                            DurableTimerClaimOrderKeyColumn,
                            1,
                            PhysicalSortDirection.Ascending)
                    ]))
                .ToArray(),
            definition.SchemaVersion,
            definition.Evolution,
            definition.LinkedProjectionLogicalName,
            definition.LinkedKey);
        var route = new BoundedQueryDeclaration(
            ElsaRuntimeStorageManifest.ClaimDueDurableTimersQuery,
            ElsaRuntimeStorageManifest.DurableTimerByClaimOrder,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual },
            QuerySortSupport.Ascending,
            QueryPagingSupport.None,
            BoundedQueryExecutionClass.ScaleBearing,
            sortFields:
            [
                new BoundedQuerySortField(
                    ElsaRuntimeStorageManifest.DurableTimerClaimOrderKeyField,
                    PhysicalSortDirection.Ascending)
            ],
            predicateFields:
            [
                new BoundedQueryPredicateField(
                    ElsaRuntimeStorageManifest.DurableTimerClaimOrderKeyField,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual })
            ]);

        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                storage.ProvisioningMode,
                PhysicalStoragePolicy.Explicit(physical),
                storage.LogicalIndexes,
                storage.BoundedQueries
                    .Where(existing => !StringComparer.Ordinal.Equals(
                        existing.Identity,
                        ElsaRuntimeStorageManifest.ClaimDueDurableTimersQuery))
                    .Append(route)
                    .ToArray(),
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }

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
            [
                ElsaRuntimeStorageManifest.ByRecurringScheduleActiveIndex,
                ElsaRuntimeStorageManifest.RecurringTriggerScheduleByNextOccurrence
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

    private static StorageUnit AddTimerPageRoute(StorageUnit unit) =>
        AddRoute(
            unit,
            ElsaRuntimeStorageManifest.DurableTimerByWorkflowAndTimerId,
            [
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                new IndexField(ElsaRuntimeStorageManifest.DurableTimerIdField)
            ],
            [
                ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex,
                ElsaRuntimeStorageManifest.ByTimerIdIndex
            ],
            [],
            ElsaRuntimeStorageManifest.PageDurableTimersByWorkflowExecutionQuery,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
            [
                new BoundedQueryPredicateField(
                    ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
            ],
            [
                new BoundedQuerySortField(
                    ElsaRuntimeStorageManifest.DurableTimerIdField,
                    PhysicalSortDirection.Ascending)
            ],
            paging: QueryPagingSupport.Cursor);

    private static StorageUnit AddSchedulePageRoutes(StorageUnit unit) =>
        AddRoute(
            AddRoute(
                unit,
                ElsaRuntimeStorageManifest.RecurringTriggerScheduleByActivationAndScheduleId,
                [
                    new IndexField(ElsaRuntimeStorageManifest.RecurringTriggerScheduleActivationIdField),
                    new IndexField(ElsaRuntimeStorageManifest.RecurringTriggerScheduleIdField)
                ],
                [
                    ElsaRuntimeStorageManifest.ByActivationIndex,
                    ElsaRuntimeStorageManifest.ByRecurringScheduleIdIndex
                ],
                [],
                ElsaRuntimeStorageManifest.PageRecurringTriggerSchedulesByPublicationQuery,
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                [
                    new BoundedQueryPredicateField(
                        ElsaRuntimeStorageManifest.RecurringTriggerScheduleActivationIdField,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
                ],
                [
                    new BoundedQuerySortField(
                        ElsaRuntimeStorageManifest.RecurringTriggerScheduleIdField,
                        PhysicalSortDirection.Ascending)
                ],
                paging: QueryPagingSupport.Cursor),
            ElsaRuntimeStorageManifest.RecurringTriggerScheduleByArtifactAndScheduleId,
            [
                new IndexField(ElsaRuntimeStorageManifest.ArtifactIdField),
                new IndexField(ElsaRuntimeStorageManifest.RecurringTriggerScheduleIdField)
            ],
            [
                ElsaRuntimeStorageManifest.ByArtifactIndex,
                ElsaRuntimeStorageManifest.ByRecurringScheduleIdIndex
            ],
            [],
            ElsaRuntimeStorageManifest.PageRecurringTriggerSchedulesByArtifactQuery,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
            [
                new BoundedQueryPredicateField(
                    ElsaRuntimeStorageManifest.ArtifactIdField,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
            ],
            [
                new BoundedQuerySortField(
                    ElsaRuntimeStorageManifest.RecurringTriggerScheduleIdField,
                    PhysicalSortDirection.Ascending)
            ],
            paging: QueryPagingSupport.Cursor);

    private static StorageUnit AddRoute(
        StorageUnit unit,
        string indexIdentity,
        IndexField[] fields,
        string[] projectedColumns,
        string[] supersededPhysicalIndexes,
        string queryIdentity,
        IReadOnlySet<PortableQueryOperation> operations,
        BoundedQueryPredicateField[] predicates,
        BoundedQuerySortField[] order,
        QueryPagingSupport paging = QueryPagingSupport.None)
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
            MissingValueBehavior.IncludedAsNull);
        var envelope = new DocumentEnvelopeDefinition();
        var physicalIndexColumns = new List<PhysicalIndexColumnDefinition>
        {
            new(envelope.StorageScopeColumn, 0)
        };
        physicalIndexColumns.AddRange(projectedColumns.Select((column, index) =>
            new PhysicalIndexColumnDefinition(column, index + 1)));
        if (paging == QueryPagingSupport.Cursor)
        {
            physicalIndexColumns.Add(new PhysicalIndexColumnDefinition(
                envelope.IdLookupKeyColumn,
                physicalIndexColumns.Count));
        }

        var physicalIndex = new PhysicalIndexDefinition(logicalIndex.Identity, physicalIndexColumns);
        var table = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            definition.ProjectedColumns
                .Select(column =>
                    projectedColumns.Contains(column.LogicalName, StringComparer.Ordinal) &&
                    column.Type == PortablePhysicalType.String
                        ? column with
                        {
                            Length = ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength
                        }
                        : column)
                .ToArray(),
            definition.Indexes
                .Where(index => !supersededPhysicalIndexes.Contains(index.LogicalName, StringComparer.Ordinal))
                .Append(physicalIndex)
                .ToArray(),
            definition.SchemaVersion,
            definition.Evolution,
            definition.LinkedProjectionLogicalName,
            definition.LinkedKey);
        var query = new BoundedQueryDeclaration(
            queryIdentity,
            logicalIndex.Identity,
            operations,
            QuerySortSupport.Ascending,
            paging,
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
