using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Persistence.Groundwork;

internal static class PostCommitOutboxGroundworkStoragePhysicalizer
{
    public static StorageManifest AddBoundedDeliveryRoutes(StorageManifest manifest) => manifest with
    {
        StorageUnits = manifest.StorageUnits.Select(unit =>
            StringComparer.Ordinal.Equals(unit.Identity.Value, ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind)
                ? AddBoundedDeliveryRoutes(unit)
                : unit).ToArray()
    };

    private static StorageUnit AddBoundedDeliveryRoutes(StorageUnit unit)
    {
        if (unit.PhysicalStorage is not { } storage ||
            storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy { Definition: var definition } ||
            definition.Form != PhysicalStorageForm.SharedDocuments)
        {
            throw new InvalidOperationException(
                "The post-commit outbox storage unit requires an explicit shared-document physicalization.");
        }

        var routes = new[]
        {
            DeliveryRoute(
                ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxQuery,
                "deliverable",
                ElsaRuntimeStorageManifest.PostCommitOutboxAvailableAtField,
                ElsaRuntimeStorageManifest.ByOutboxAvailableAtIndex,
                []),
            DeliveryRoute(
                ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxByWorkflowQuery,
                "deliverable-by-workflow",
                ElsaRuntimeStorageManifest.PostCommitOutboxAvailableAtField,
                ElsaRuntimeStorageManifest.ByOutboxAvailableAtIndex,
                [Filter.Workflow]),
            DeliveryRoute(
                ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxByIntentKindQuery,
                "deliverable-by-intent-kind",
                ElsaRuntimeStorageManifest.PostCommitOutboxAvailableAtField,
                ElsaRuntimeStorageManifest.ByOutboxAvailableAtIndex,
                [Filter.IntentKind]),
            DeliveryRoute(
                ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxByWorkflowAndIntentKindQuery,
                "deliverable-by-workflow-and-intent-kind",
                ElsaRuntimeStorageManifest.PostCommitOutboxAvailableAtField,
                ElsaRuntimeStorageManifest.ByOutboxAvailableAtIndex,
                [Filter.Workflow, Filter.IntentKind]),
            ImmediateRoute(
                ElsaRuntimeStorageManifest.ListImmediatePostCommitOutboxQuery,
                "immediate",
                []),
            ImmediateRoute(
                ElsaRuntimeStorageManifest.ListImmediatePostCommitOutboxByWorkflowQuery,
                "immediate-by-workflow",
                [Filter.Workflow]),
            ImmediateRoute(
                ElsaRuntimeStorageManifest.ListImmediatePostCommitOutboxByIntentKindQuery,
                "immediate-by-intent-kind",
                [Filter.IntentKind]),
            ImmediateRoute(
                ElsaRuntimeStorageManifest.ListImmediatePostCommitOutboxByWorkflowAndIntentKindQuery,
                "immediate-by-workflow-and-intent-kind",
                [Filter.Workflow, Filter.IntentKind]),
            DeliveryRoute(
                ElsaRuntimeStorageManifest.ListExpiredPostCommitOutboxClaimsQuery,
                "expired-claims",
                ElsaRuntimeStorageManifest.PostCommitOutboxVisibleAfterField,
                ElsaRuntimeStorageManifest.ByOutboxVisibleAfterIndex,
                []),
            DeliveryRoute(
                ElsaRuntimeStorageManifest.ListExpiredPostCommitOutboxClaimsByWorkflowQuery,
                "expired-claims-by-workflow",
                ElsaRuntimeStorageManifest.PostCommitOutboxVisibleAfterField,
                ElsaRuntimeStorageManifest.ByOutboxVisibleAfterIndex,
                [Filter.Workflow]),
            DeliveryRoute(
                ElsaRuntimeStorageManifest.ListExpiredPostCommitOutboxClaimsByIntentKindQuery,
                "expired-claims-by-intent-kind",
                ElsaRuntimeStorageManifest.PostCommitOutboxVisibleAfterField,
                ElsaRuntimeStorageManifest.ByOutboxVisibleAfterIndex,
                [Filter.IntentKind]),
            DeliveryRoute(
                ElsaRuntimeStorageManifest.ListExpiredPostCommitOutboxClaimsByWorkflowAndIntentKindQuery,
                "expired-claims-by-workflow-and-intent-kind",
                ElsaRuntimeStorageManifest.PostCommitOutboxVisibleAfterField,
                ElsaRuntimeStorageManifest.ByOutboxVisibleAfterIndex,
                [Filter.Workflow, Filter.IntentKind])
        };
        var projectedColumns = definition.ProjectedColumns
            .Select(column => column.LogicalName switch
            {
                ElsaRuntimeStorageManifest.ByOutboxStatusIndex => column with
                {
                    Type = PortablePhysicalType.Int32,
                    Length = null
                },
                ElsaRuntimeStorageManifest.ByOutboxIntentKindIndex => column with
                {
                    Length = RuntimePostCommitIntent.MaximumKindLength
                },
                _ => column
            })
            .ToArray();
        var augmentedDefinition = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            projectedColumns,
            definition.Indexes.Concat(routes.Select(route => route.PhysicalIndex)).ToArray(),
            definition.SchemaVersion,
            definition.Evolution,
            definition.LinkedProjectionLogicalName,
            definition.LinkedKey);

        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                storage.ProvisioningMode,
                PhysicalStoragePolicy.Explicit(augmentedDefinition),
                storage.LogicalIndexes.Concat(routes.Select(route => route.LogicalIndex)).ToArray(),
                storage.BoundedQueries.Concat(routes.Select(route => route.Query)).ToArray(),
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }

    private static OutboxDeliveryRoute DeliveryRoute(
        string queryIdentity,
        string indexIdentity,
        string temporalField,
        string temporalProjectedColumn,
        IReadOnlyList<Filter> filters,
        MissingValueBehavior missingValueBehavior = MissingValueBehavior.Excluded,
        PortableQueryOperation temporalOperation = PortableQueryOperation.LessThanOrEqual)
    {
        var filterFields = filters.Select(Field).ToArray();
        IndexField[] fields =
        [
            new IndexField(
                ElsaRuntimeStorageManifest.PostCommitOutboxStatusField,
                IndexValueKind.Number),
            .. filterFields.Select(filter => new IndexField(filter.Path)),
            new IndexField(temporalField, IndexValueKind.DateTime),
            new IndexField(ElsaRuntimeStorageManifest.PostCommitOutboxRecordedAtField, IndexValueKind.DateTime)
        ];
        var logicalIndex = new LogicalIndexDeclaration(
            $"by-{indexIdentity}-status-time-recorded",
            fields,
            IndexValueKind.Keyword,
            isUnique: false,
            missingValueBehavior);
        string[] projectedColumns =
        [
            ElsaRuntimeStorageManifest.ByOutboxStatusIndex,
            .. filterFields.Select(filter => filter.ProjectedColumn),
            temporalProjectedColumn,
            ElsaRuntimeStorageManifest.ByOutboxRecordedAtIndex
        ];
        var physicalIndex = new PhysicalIndexDefinition(
            logicalIndex.Identity,
            [
                new PhysicalIndexColumnDefinition(new DocumentEnvelopeDefinition().StorageScopeColumn, 0),
                .. projectedColumns.Select((column, index) =>
                    new PhysicalIndexColumnDefinition(column, index + 1))
            ],
            missingValueBehavior: missingValueBehavior);
        var predicateFields = new List<BoundedQueryPredicateField>
        {
            Equal(ElsaRuntimeStorageManifest.PostCommitOutboxStatusField)
        };
        predicateFields.AddRange(filterFields.Select(filter => Equal(filter.Path)));
        predicateFields.Add(new BoundedQueryPredicateField(
            temporalField,
            new HashSet<PortableQueryOperation> { temporalOperation }));

        var supportedOperations = new HashSet<PortableQueryOperation>
        {
            PortableQueryOperation.Equal
        };
        supportedOperations.Add(temporalOperation);

        var query = new BoundedQueryDeclaration(
            queryIdentity,
            logicalIndex.Identity,
            supportedOperations,
            QuerySortSupport.Ascending,
            QueryPagingSupport.None,
            sortFields: fields
                .Select(field => new BoundedQuerySortField(field.Path, PhysicalSortDirection.Ascending))
                .ToArray(),
            predicateFields: predicateFields);
        return new OutboxDeliveryRoute(logicalIndex, physicalIndex, query);
    }

    private static OutboxDeliveryRoute ImmediateRoute(
        string queryIdentity,
        string indexIdentity,
        IReadOnlyList<Filter> filters) =>
        DeliveryRoute(
            queryIdentity,
            indexIdentity,
            ElsaRuntimeStorageManifest.PostCommitOutboxAvailableAtField,
            ElsaRuntimeStorageManifest.ByOutboxAvailableAtIndex,
            filters,
            MissingValueBehavior.IncludedAsNull,
            PortableQueryOperation.Equal);

    private static FilterField Field(Filter filter) => filter switch
    {
        Filter.Workflow => new FilterField(
            ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
            ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex),
        Filter.IntentKind => new FilterField(
            ElsaRuntimeStorageManifest.PostCommitOutboxIntentKindField,
            ElsaRuntimeStorageManifest.ByOutboxIntentKindIndex),
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null)
    };

    private static BoundedQueryPredicateField Equal(string path) => new(
        path,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal });

    private enum Filter
    {
        Workflow,
        IntentKind
    }

    private sealed record FilterField(string Path, string ProjectedColumn);

    private sealed record OutboxDeliveryRoute(
        LogicalIndexDeclaration LogicalIndex,
        PhysicalIndexDefinition PhysicalIndex,
        BoundedQueryDeclaration Query);
}
