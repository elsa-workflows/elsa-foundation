using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Persistence.Groundwork;

/// <summary>
/// Gives post-commit delivery and claiming one provider-side candidate set each. The derived timestamps are
/// maintained with the document, so eligibility, ordering, and limiting do not require a client-side union.
/// </summary>
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
            CandidateRoute(
                ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxQuery,
                "deliverable",
                ElsaRuntimeStorageManifest.PostCommitOutboxDeliverableAtField,
                ElsaRuntimeStorageManifest.ByOutboxDeliverableAtIndex,
                []),
            CandidateRoute(
                ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxByWorkflowQuery,
                "deliverable-by-workflow",
                ElsaRuntimeStorageManifest.PostCommitOutboxDeliverableAtField,
                ElsaRuntimeStorageManifest.ByOutboxDeliverableAtIndex,
                [Filter.Workflow]),
            CandidateRoute(
                ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxByIntentKindQuery,
                "deliverable-by-intent-kind",
                ElsaRuntimeStorageManifest.PostCommitOutboxDeliverableAtField,
                ElsaRuntimeStorageManifest.ByOutboxDeliverableAtIndex,
                [Filter.IntentKind]),
            CandidateRoute(
                ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxByWorkflowAndIntentKindQuery,
                "deliverable-by-workflow-and-intent-kind",
                ElsaRuntimeStorageManifest.PostCommitOutboxDeliverableAtField,
                ElsaRuntimeStorageManifest.ByOutboxDeliverableAtIndex,
                [Filter.Workflow, Filter.IntentKind]),
            CandidateRoute(
                ElsaRuntimeStorageManifest.ListClaimablePostCommitOutboxQuery,
                "claimable",
                ElsaRuntimeStorageManifest.PostCommitOutboxClaimableAtField,
                ElsaRuntimeStorageManifest.ByOutboxClaimableAtIndex,
                []),
            CandidateRoute(
                ElsaRuntimeStorageManifest.ListClaimablePostCommitOutboxByWorkflowQuery,
                "claimable-by-workflow",
                ElsaRuntimeStorageManifest.PostCommitOutboxClaimableAtField,
                ElsaRuntimeStorageManifest.ByOutboxClaimableAtIndex,
                [Filter.Workflow]),
            CandidateRoute(
                ElsaRuntimeStorageManifest.ListClaimablePostCommitOutboxByIntentKindQuery,
                "claimable-by-intent-kind",
                ElsaRuntimeStorageManifest.PostCommitOutboxClaimableAtField,
                ElsaRuntimeStorageManifest.ByOutboxClaimableAtIndex,
                [Filter.IntentKind]),
            CandidateRoute(
                ElsaRuntimeStorageManifest.ListClaimablePostCommitOutboxByWorkflowAndIntentKindQuery,
                "claimable-by-workflow-and-intent-kind",
                ElsaRuntimeStorageManifest.PostCommitOutboxClaimableAtField,
                ElsaRuntimeStorageManifest.ByOutboxClaimableAtIndex,
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
                ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex => column with
                {
                    Length = ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength
                },
                ElsaRuntimeStorageManifest.ByOutboxItemIdIndex => column with
                {
                    Length = ElsaRuntimeStorageManifest.PostCommitOutboxItemIdProjectionLength
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

    private static OutboxCandidateRoute CandidateRoute(
        string queryIdentity,
        string indexIdentity,
        string candidateAtField,
        string candidateAtProjectedColumn,
        IReadOnlyList<Filter> filters)
    {
        var filterFields = filters.Select(Field).ToArray();
        IndexField[] fields =
        [
            .. filterFields.Select(filter => new IndexField(filter.Path)),
            new IndexField(candidateAtField, IndexValueKind.DateTime),
            new IndexField(ElsaRuntimeStorageManifest.PostCommitOutboxRecordedAtField, IndexValueKind.DateTime),
            new IndexField(ElsaRuntimeStorageManifest.PostCommitOutboxItemIdField)
        ];
        var logicalIndex = new LogicalIndexDeclaration(
            $"by-{indexIdentity}-time-recorded-id",
            fields,
            IndexValueKind.Keyword,
            isUnique: false);
        string[] projectedColumns =
        [
            .. filterFields.Select(filter => filter.ProjectedColumn),
            candidateAtProjectedColumn,
            ElsaRuntimeStorageManifest.ByOutboxRecordedAtIndex,
            ElsaRuntimeStorageManifest.ByOutboxItemIdIndex
        ];
        var physicalIndex = new PhysicalIndexDefinition(
            logicalIndex.Identity,
            [
                new PhysicalIndexColumnDefinition(new DocumentEnvelopeDefinition().StorageScopeColumn, 0),
                .. projectedColumns.Select((column, index) =>
                    new PhysicalIndexColumnDefinition(column, index + 1))
            ]);
        var predicateFields = new List<BoundedQueryPredicateField>();
        predicateFields.AddRange(filterFields.Select(filter => Equal(filter.Path)));
        predicateFields.Add(new BoundedQueryPredicateField(
            candidateAtField,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual }));
        var supportedOperations = new HashSet<PortableQueryOperation>
        {
            PortableQueryOperation.Equal,
            PortableQueryOperation.LessThanOrEqual
        };
        var sortFields = fields
            .Select(field => new BoundedQuerySortField(field.Path, PhysicalSortDirection.Ascending))
            .ToArray();
        var query = new BoundedQueryDeclaration(
            queryIdentity,
            logicalIndex.Identity,
            supportedOperations,
            QuerySortSupport.Ascending,
            QueryPagingSupport.None,
            sortFields: sortFields,
            predicateFields: predicateFields);
        return new OutboxCandidateRoute(logicalIndex, physicalIndex, query);
    }

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

    private sealed record OutboxCandidateRoute(
        LogicalIndexDeclaration LogicalIndex,
        PhysicalIndexDefinition PhysicalIndex,
        BoundedQueryDeclaration Query);
}
