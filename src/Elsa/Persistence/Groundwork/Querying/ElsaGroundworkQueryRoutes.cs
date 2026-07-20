using Groundwork.Core.Manifests;
using Groundwork.Core.Indexing;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Elsa.Persistence.Groundwork.Composition;

namespace Elsa.Persistence.Groundwork;

/// <summary>
/// The closed B7 query inventory. It is deliberately separate from the store implementations so composition can
/// reject a missing provider route before an adapter falls back to loading a document kind in full.
/// </summary>
public static class ElsaGroundworkQueryRoutes
{
    /// <summary>The hard upper bound selected for ordinary runtime collection reads.</summary>
    public const int MaximumResultCount = 500;

    /// <summary>Every B7 query shape from the frozen coverage-ledger denominator.</summary>
    public static IReadOnlyList<ElsaGroundworkQueryRoute> All { get; } =
    [
        Primary("runtime-activity-execution-inspection", "find-by-workflow-and-activity", ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind, "primary.activity-execution-inspection.workflow-and-activity.v1"),
        Route("runtime-activity-execution-inspection", "list-summaries-by-workflow-bounded", ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind, Documents(), BoundedOrdered(ElsaRuntimeStorageManifest.PageActivityExecutionInspectionSummariesQuery, ElsaRuntimeStorageManifest.ActivityExecutionInspectionOrderIndex, ElsaGroundworkQueryContinuation.Cursor, [ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryExecutionSequenceField, ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryScheduledAtField, ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryActivityExecutionIdField], Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField))),
        Route("runtime-activity-execution-inspection", "list-descendants-by-parent-bounded", ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind, Documents(),
            BoundedOrderedDirections(
                ElsaRuntimeStorageManifest.FindLatestActivityExecutionHierarchyByWorkflowQuery,
                ElsaRuntimeStorageManifest.ActivityExecutionHierarchyLatestByWorkflowIndex,
                ElsaGroundworkQueryContinuation.None,
                [
                    new ElsaGroundworkQueryOrder(
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField,
                        PhysicalSortDirection.Descending),
                    new ElsaGroundworkQueryOrder(
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyActivityExecutionIdField,
                        PhysicalSortDirection.Descending)
                ],
                Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField)),
            BoundedOrderedDirections(
                ElsaRuntimeStorageManifest.PageActivityExecutionHierarchyByWorkflowQuery,
                ElsaRuntimeStorageManifest.ActivityExecutionHierarchyPageByWorkflowIndex,
                ElsaGroundworkQueryContinuation.Cursor,
                [
                    new ElsaGroundworkQueryOrder(
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField,
                        PhysicalSortDirection.Descending),
                    new ElsaGroundworkQueryOrder(
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyActivityExecutionIdField,
                        PhysicalSortDirection.Descending)
                ],
                Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField)),
            BoundedOrdered(
                ElsaRuntimeStorageManifest.PageActivityExecutionHierarchyByScopeQuery,
                ElsaRuntimeStorageManifest.ActivityExecutionHierarchyByScopeAndOrderIndex,
                ElsaGroundworkQueryContinuation.Cursor,
                [
                    ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField,
                    ElsaRuntimeStorageManifest.ActivityExecutionHierarchyActivityExecutionIdField
                ],
                Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                Equal(ElsaRuntimeStorageManifest.ExecutionScopeIdField),
                Equal(ElsaRuntimeStorageManifest.ActivityExecutionHierarchyIsScopeRootField),
                LessThanOrEqual(ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField))),

        Primary("runtime-activity-execution-state", "find-by-workflow-and-activity", ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind, "primary.activity-execution-state.workflow-and-activity.v1"),
        Route("runtime-activity-execution-state", "list-by-workflow-bounded", ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind, Documents(), BoundedOrdered(ElsaRuntimeStorageManifest.PageActivityExecutionStatesByWorkflowExecutionQuery, ElsaRuntimeStorageManifest.ActivityExecutionStateByWorkflowAndActivityExecutionId, ElsaGroundworkQueryContinuation.Cursor, [ElsaRuntimeStorageManifest.ActivityExecutionIdField], Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField))),
        Route("runtime-activity-execution-state", "list-by-parent-bounded", ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind, Documents(), BoundedOrdered(ElsaRuntimeStorageManifest.PageActivityExecutionStatesByParentQuery, ElsaRuntimeStorageManifest.ActivityExecutionStateByWorkflowParentAndActivityExecutionId, ElsaGroundworkQueryContinuation.Cursor, [ElsaRuntimeStorageManifest.ActivityExecutionIdField], Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField), Equal(ElsaRuntimeStorageManifest.ParentActivityExecutionIdField))),

        Primary("runtime-bookmark-state", "find-by-workflow-and-bookmark", ElsaRuntimeStorageManifest.BookmarkStateDocumentKind, "primary.bookmark-state.workflow-and-bookmark.v1"),
        Route("runtime-bookmark-state", "list-by-workflow-bounded", ElsaRuntimeStorageManifest.BookmarkStateDocumentKind, Documents(), BoundedOrdered(ElsaRuntimeStorageManifest.ListBookmarksByWorkflowExecutionQuery, ElsaRuntimeStorageManifest.BookmarkStateByWorkflowAndBookmarkId, ElsaGroundworkQueryContinuation.Cursor, [ElsaRuntimeStorageManifest.BookmarkIdField], Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField))),
        Route("runtime-bookmark-state", "list-by-stimulus-and-type-bounded", ElsaRuntimeStorageManifest.BookmarkStateDocumentKind, Documents(), BoundedOrdered(ElsaRuntimeStorageManifest.ListBookmarksByStimulusAndTypeQuery, ElsaRuntimeStorageManifest.BookmarkStateByStimulusAndTypeAndIdentity, ElsaGroundworkQueryContinuation.Cursor, [ElsaRuntimeStorageManifest.WorkflowExecutionIdField, ElsaRuntimeStorageManifest.BookmarkIdField], Equal(ElsaRuntimeStorageManifest.BookmarkStimulusLookupKeyField))),
        Route("runtime-bookmark-state", "list-by-stimulus-type-bounded", ElsaRuntimeStorageManifest.BookmarkStateDocumentKind, Documents(), BoundedOrdered(ElsaRuntimeStorageManifest.ListBookmarksByStimulusTypeQuery, ElsaRuntimeStorageManifest.BookmarkStateByStimulusTypeAndIdentity, ElsaGroundworkQueryContinuation.Cursor, [ElsaRuntimeStorageManifest.WorkflowExecutionIdField, ElsaRuntimeStorageManifest.BookmarkIdField], Equal(ElsaRuntimeStorageManifest.BookmarkStimulusTypeLookupKeyField))),

        Primary("runtime-durable-value-state", "find-by-workflow-and-value", ElsaRuntimeStorageManifest.DurableValueStateDocumentKind, "primary.durable-value-state.workflow-and-value.v1"),
        Route("runtime-durable-value-state", "list-by-workflow-bounded", ElsaRuntimeStorageManifest.DurableValueStateDocumentKind, Documents(), BoundedOrdered(ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery, ElsaRuntimeStorageManifest.DurableValueStateByWorkflowAndValueId, ElsaGroundworkQueryContinuation.Cursor, [ElsaRuntimeStorageManifest.DurableValueIdField], Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField))),

        Primary("runtime-executable-source-reference", "find-by-reference", ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind, "primary.workflow-executable-source-reference.reference.v1"),
        Route("runtime-executable-source-reference", "list-by-artifact-bounded", ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind, Documents(), BoundedOrdered(ElsaRuntimeStorageManifest.PageWorkflowExecutableSourceReferencesByArtifactQuery, ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByArtifactAndId, ElsaGroundworkQueryContinuation.Cursor, [ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceIdField], Equal(ElsaRuntimeStorageManifest.ArtifactIdField))),
        Route("runtime-executable-source-reference", "list-live-by-scope-bounded", ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind, Documents(),
            BoundedOrdered(ElsaRuntimeStorageManifest.PageWorkflowExecutableSourceReferencesQuery, ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByCollectionAndId, ElsaGroundworkQueryContinuation.Cursor, [ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceIdField], Equal(ElsaRuntimeStorageManifest.CollectionField)),
            BoundedOrdered(ElsaRuntimeStorageManifest.PageWorkflowExecutableSourceReferencesByScopeQuery, ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByScopeAndId, ElsaGroundworkQueryContinuation.Cursor, [ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceIdField], Equal(ElsaRuntimeStorageManifest.ScopeField)),
            BoundedOrdered(ElsaRuntimeStorageManifest.PageLiveWorkflowExecutableSourceReferencesQuery, ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceLiveByCollectionAndId, ElsaGroundworkQueryContinuation.Cursor, [ElsaRuntimeStorageManifest.ExpiresAtField, ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceIdField], Equal(ElsaRuntimeStorageManifest.CollectionField), Equal(ElsaRuntimeStorageManifest.IsRetiredField), GreaterThan(ElsaRuntimeStorageManifest.ExpiresAtField)),
            BoundedOrdered(ElsaRuntimeStorageManifest.PageLiveWorkflowExecutableSourceReferencesByScopeQuery, ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceLiveByScopeAndId, ElsaGroundworkQueryContinuation.Cursor, [ElsaRuntimeStorageManifest.ExpiresAtField, ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceIdField], Equal(ElsaRuntimeStorageManifest.ScopeField), Equal(ElsaRuntimeStorageManifest.IsRetiredField), GreaterThan(ElsaRuntimeStorageManifest.ExpiresAtField))),
        Route("runtime-executable-source-reference", "delete-expired-or-retired-bounded", ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind, Documents(),
            BoundedOrdered(ElsaRuntimeStorageManifest.BatchExpiredWorkflowExecutableSourceReferencesQuery, ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByExpiryAndId, ElsaGroundworkQueryContinuation.None, [ElsaRuntimeStorageManifest.ExpiresAtField, ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceIdField], LessThanOrEqual(ElsaRuntimeStorageManifest.ExpiresAtField)),
            BoundedOrdered(ElsaRuntimeStorageManifest.BatchRetiredWorkflowExecutableSourceReferencesQuery, ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByRetiredAndId, ElsaGroundworkQueryContinuation.None, [ElsaRuntimeStorageManifest.ExpiresAtField, ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceIdField], Equal(ElsaRuntimeStorageManifest.IsRetiredField), GreaterThan(ElsaRuntimeStorageManifest.ExpiresAtField))),
        Route("runtime-executable-source-reference", "list-unreferenced-artifacts-bounded", ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind, Documents(), BoundedOrdered(ElsaRuntimeStorageManifest.FindLiveWorkflowExecutableSourceReferenceByArtifactQuery, ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceLiveByArtifactAndId, ElsaGroundworkQueryContinuation.None, [ElsaRuntimeStorageManifest.ExpiresAtField, ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceIdField], Equal(ElsaRuntimeStorageManifest.ArtifactIdField), Equal(ElsaRuntimeStorageManifest.IsRetiredField), GreaterThan(ElsaRuntimeStorageManifest.ExpiresAtField))),

        Primary("runtime-workflow-executable", "find-by-artifact", ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind, "primary.workflow-executable.artifact.v1"),
        Route("runtime-workflow-executable", "list-bounded", ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind, Documents(), BoundedOrdered(ElsaRuntimeStorageManifest.PageWorkflowExecutablesQuery, ElsaRuntimeStorageManifest.WorkflowExecutableByCollectionAndId, ElsaGroundworkQueryContinuation.Cursor, [ElsaRuntimeStorageManifest.WorkflowExecutableArtifactIdField], Equal(ElsaRuntimeStorageManifest.CollectionField))),
        Route("runtime-workflow-executable", "find-template-by-hash", ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind, Documents(),
            Bounded(ElsaRuntimeStorageManifest.FindExecutableActivityTemplateByHashQuery, ElsaRuntimeStorageManifest.ExecutableActivityTemplateByHash, Equal(ElsaRuntimeStorageManifest.TemplateHashField)),
            BoundedOrdered(ElsaRuntimeStorageManifest.PageExecutableActivityTemplatesQuery, ElsaRuntimeStorageManifest.ExecutableActivityTemplateByCollectionAndId, ElsaGroundworkQueryContinuation.Cursor, [ElsaRuntimeStorageManifest.ExecutableActivityTemplateIdField], Equal(ElsaRuntimeStorageManifest.CollectionField))),

        Primary("runtime-workflow-execution-state", "find-by-execution", ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, "primary.workflow-execution-state.execution.v1"),
        Route("runtime-workflow-execution-state", "query-page-bounded", ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, Documents(), WorkflowExecutionHistoryRoute()),
        Route("runtime-workflow-execution-state", "page-faulted-for-attention-bounded", ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, Documents(), FaultedWorkflowExecutionAttentionRoute()),
        Route("runtime-workflow-execution-state", "list-pinned-artifact-ids-bounded", ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, Projection(),
            BoundedOrdered(
                ElsaRuntimeStorageManifest.PagePinnedExecutableArtifactIdsQuery,
                ElsaRuntimeStorageManifest.WorkflowExecutionPinnedArtifactOrderIndex,
                ElsaGroundworkQueryContinuation.Offset,
                [
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField,
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField
                ],
                ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField,
                Equal(ElsaRuntimeStorageManifest.CollectionField))),
        Route("runtime-workflow-execution-state", "list-all-replaced-by-bounded-page", ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, Documents(), WorkflowExecutionHistoryRoute()),

        Route("runtime-incident-state", "page-attention-by-status-bounded", ElsaRuntimeStorageManifest.IncidentStateDocumentKind, Documents(), BoundedOrdered(
            ElsaRuntimeStorageManifest.PageAttentionIncidentsByStatusQuery,
            ElsaRuntimeStorageManifest.IncidentAttentionStatusOrderIndex,
            ElsaGroundworkQueryContinuation.Cursor,
            [
                ElsaRuntimeStorageManifest.CreatedAtField,
                ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
                ElsaRuntimeStorageManifest.IncidentIdField
            ],
            Equal(ElsaRuntimeStorageManifest.StatusField))),

        Route("runtime-trigger-binding", "list-by-publication-bounded", ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind, Documents(), BoundedOrdered(ElsaRuntimeStorageManifest.ListTriggerBindingsByPublicationQuery, ElsaRuntimeStorageManifest.WorkflowTriggerBindingByPublicationAndId, ElsaGroundworkQueryContinuation.Cursor, [ElsaRuntimeStorageManifest.TriggerBindingIdField], Equal(ElsaRuntimeStorageManifest.PublicationIdField))),
        Route("runtime-trigger-binding", "list-by-stimulus-bounded", ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind, Documents(), BoundedOrdered(ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusAndTypeQuery, ElsaRuntimeStorageManifest.WorkflowTriggerBindingByStimulusAndType, ElsaGroundworkQueryContinuation.Cursor, [ElsaRuntimeStorageManifest.TriggerBindingIdField], Equal(ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusLookupKeyField), Equal(ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField))),
        Route("runtime-trigger-binding", "list-by-artifact-bounded", ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind, Documents(), BoundedOrdered(ElsaRuntimeStorageManifest.ListTriggerBindingsByArtifactQuery, ElsaRuntimeStorageManifest.WorkflowTriggerBindingByArtifactAndId, ElsaGroundworkQueryContinuation.Cursor, [ElsaRuntimeStorageManifest.TriggerBindingIdField], Equal(ElsaRuntimeStorageManifest.ArtifactIdField))),
        Route("runtime-trigger-binding", "list-by-stimulus-type-bounded", ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind, Documents(), BoundedOrdered(ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusTypeQuery, ElsaRuntimeStorageManifest.WorkflowTriggerBindingByStimulusTypeAndActive, ElsaGroundworkQueryContinuation.Cursor, [ElsaRuntimeStorageManifest.TriggerBindingIdField], Equal(ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusTypeLookupKeyField), Equal(ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField)))
    ];

    internal static StorageManifest AddPhysicalRoutes(StorageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return manifest with
        {
            StorageUnits = manifest.StorageUnits.Select(unit =>
                unit.Identity.Value switch
                {
                    ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind =>
                        PhysicalizeActivityExecutionState(unit),
                    ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind =>
                        PhysicalizeActivityExecutionInspection(unit),
                    ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind =>
                        PhysicalizeActivityExecutionHierarchy(unit),
                    ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind =>
                        PhysicalizeEnvelopeOrderedRoutes(
                            unit,
                            Field(
                                ElsaRuntimeStorageManifest.WorkflowExecutableArtifactIdField,
                                ElsaRuntimeStorageManifest.ByArtifactIndex,
                                length: ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength),
                            new EnvelopeOrderedRoute(
                                ElsaRuntimeStorageManifest.WorkflowExecutableByCollectionAndId,
                                [
                                    Field(
                                        ElsaRuntimeStorageManifest.CollectionField,
                                        ElsaRuntimeStorageManifest.ByCollectionIndex,
                                        length: ElsaRuntimeStorageManifest.RuntimeCollectionProjectionLength)
                                ],
                                UsesCursorPaging: true)),
                    ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind =>
                        PhysicalizeEnvelopeOrderedRoutes(
                            unit,
                            Field(
                                ElsaRuntimeStorageManifest.ExecutableActivityTemplateIdField,
                                ElsaRuntimeStorageManifest.ByTemplateIdIndex,
                                length: ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength),
                            new EnvelopeOrderedRoute(
                                ElsaRuntimeStorageManifest.ExecutableActivityTemplateByCollectionAndId,
                                [
                                    Field(
                                        ElsaRuntimeStorageManifest.CollectionField,
                                        ElsaRuntimeStorageManifest.ByCollectionIndex,
                                        length: ElsaRuntimeStorageManifest.RuntimeCollectionProjectionLength)
                                ],
                                UsesCursorPaging: true)),
                    ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind =>
                        PhysicalizeWorkflowExecutableSourceReferences(unit),
                    ElsaRuntimeStorageManifest.BookmarkStateDocumentKind =>
                        PhysicalizeWorkflowIdentityRoute(
                            unit,
                            ElsaRuntimeStorageManifest.BookmarkStateByWorkflowAndBookmarkId,
                            ElsaRuntimeStorageManifest.BookmarkIdField,
                            "bookmark_id"),
                    ElsaRuntimeStorageManifest.DurableValueStateDocumentKind =>
                        PhysicalizeWorkflowIdentityRoute(
                            unit,
                            ElsaRuntimeStorageManifest.DurableValueStateByWorkflowAndValueId,
                            ElsaRuntimeStorageManifest.DurableValueIdField,
                            "durable_value_id"),
                    ElsaRuntimeStorageManifest.IncidentStateDocumentKind =>
                        PhysicalizeEnvelopeOrderedRoutes(
                            unit,
                            Field(
                                ElsaRuntimeStorageManifest.IncidentIdField,
                                ElsaRuntimeStorageManifest.IncidentAttentionIncidentIdIndex,
                                length: ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength),
                            new EnvelopeOrderedRoute(
                                ElsaRuntimeStorageManifest.IncidentAttentionStatusOrderIndex,
                                [
                                    Field(
                                        ElsaRuntimeStorageManifest.StatusField,
                                        ElsaRuntimeStorageManifest.IncidentAttentionStatusIndex,
                                        IndexValueKind.Number),
                                    Field(
                                        ElsaRuntimeStorageManifest.CreatedAtField,
                                        ElsaRuntimeStorageManifest.IncidentAttentionCreatedAtIndex,
                                        IndexValueKind.DateTime),
                                    Field(
                                        ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
                                        ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex,
                                        length: ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength)
                                ],
                                UsesCursorPaging: true)),
                    _ => Physicalize(unit)
                }).ToArray()
        };
    }

    private static StorageUnit Physicalize(StorageUnit unit)
    {
        var physicalRoutes = All
            .Where(route => route.Kind == ElsaGroundworkQueryRouteKind.BoundedRoute && route.DocumentKind == unit.Identity.Value)
            .SelectMany(route => route.PhysicalRoutes)
            .GroupBy(route => route.Identity, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        if (physicalRoutes.Length == 0)
            return unit;

        var storage = unit.PhysicalStorage ?? throw new InvalidOperationException(
            $"The B7 storage unit '{unit.Identity.Value}' requires physical storage before routes can be admitted.");
        if (storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy)
            throw new InvalidOperationException(
                $"The B7 storage unit '{unit.Identity.Value}' requires an explicit physical storage policy.");

        var routeIdentities = physicalRoutes.Select(route => route.Identity).ToHashSet(StringComparer.Ordinal);
        var availableIndexes = storage.LogicalIndexes.Select(index => index.Identity).ToHashSet(StringComparer.Ordinal);
        var missingIndex = physicalRoutes.Select(route => route.IndexIdentity).FirstOrDefault(index => !availableIndexes.Contains(index));
        if (missingIndex is not null)
            throw new InvalidOperationException(
                $"The B7 storage unit '{unit.Identity.Value}' does not declare required logical index '{missingIndex}'.");

        var routes = physicalRoutes.Select(route =>
            storage.BoundedQueries.FirstOrDefault(existing =>
                StringComparer.Ordinal.Equals(existing.Identity, route.Identity) &&
                StringComparer.Ordinal.Equals(existing.IndexIdentity, route.IndexIdentity) &&
                existing.ResidualPredicateFields.Count > 0)
            ?? ToBoundedQuery(route));

        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                storage.ProvisioningMode,
                storage.Policy,
                storage.LogicalIndexes,
                storage.BoundedQueries
                    .Where(query => !routeIdentities.Contains(query.Identity))
                    .Concat(routes)
                    .ToArray(),
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }

    private static StorageUnit PhysicalizeActivityExecutionState(StorageUnit unit)
    {
        var storage = unit.PhysicalStorage ?? throw new InvalidOperationException(
            "The activity-execution-state B7 routes require physical storage before route admission.");
        if (storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy { Definition: var definition } ||
            definition.Form != PhysicalStorageForm.SharedDocuments)
        {
            throw new InvalidOperationException(
                "The activity-execution-state B7 routes require explicit shared-document physicalization.");
        }

        var activityId = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.ActivityExecutionStateByWorkflowAndActivityExecutionId,
            [
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                new IndexField(ElsaRuntimeStorageManifest.ActivityExecutionIdField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var parent = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.ActivityExecutionStateByWorkflowParentAndActivityExecutionId,
            [
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                new IndexField(ElsaRuntimeStorageManifest.ParentActivityExecutionIdField),
                new IndexField(ElsaRuntimeStorageManifest.ActivityExecutionIdField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        const string activityIdColumn = "activity_execution_id";
        var projected = definition.ProjectedColumns
            .Where(column => column.Path != ElsaRuntimeStorageManifest.ActivityExecutionIdField)
            .Select(column => column.Path is
                ElsaRuntimeStorageManifest.WorkflowExecutionIdField or
                ElsaRuntimeStorageManifest.ParentActivityExecutionIdField
                ? column with
                {
                    Length = ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength
                }
                : column)
            .Append(new ProjectedColumnDefinition(
                activityIdColumn,
                ElsaRuntimeStorageManifest.ActivityExecutionIdField,
                PortablePhysicalType.String,
                Length: ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength))
            .ToArray();
        var physical = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            projected,
            definition.Indexes
                .Where(index => index.LogicalName is not (
                    ElsaRuntimeStorageManifest.ActivityExecutionStateByWorkflowAndActivityExecutionId or
                    ElsaRuntimeStorageManifest.ActivityExecutionStateByWorkflowParentAndActivityExecutionId))
                .Concat(
                [
                    PhysicalIndex(activityId, [ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, activityIdColumn]),
                    PhysicalIndex(parent, [ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, ElsaRuntimeStorageManifest.ByParentActivityExecutionIndex, activityIdColumn])
                ])
                .ToArray(),
            definition.SchemaVersion,
            definition.Evolution,
            definition.LinkedProjectionLogicalName,
            definition.LinkedKey);
        var augmented = unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                storage.ProvisioningMode,
                PhysicalStoragePolicy.Explicit(physical),
                storage.LogicalIndexes
                    .Where(index => index.Identity is not (
                        ElsaRuntimeStorageManifest.ActivityExecutionStateByWorkflowAndActivityExecutionId or
                        ElsaRuntimeStorageManifest.ActivityExecutionStateByWorkflowParentAndActivityExecutionId))
                    .Concat([activityId, parent])
                    .ToArray(),
                storage.BoundedQueries,
                storage.NameOverrides,
                storage.BoundedMutations)
        };

        return Physicalize(augmented);
    }

    private static StorageUnit PhysicalizeWorkflowIdentityRoute(
        StorageUnit unit,
        string indexIdentity,
        string identityPath,
        string identityColumn)
    {
        var storage = unit.PhysicalStorage ?? throw new InvalidOperationException(
            $"The '{unit.Identity.Value}' B7 routes require physical storage before route admission.");
        if (storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy { Definition: var definition } ||
            definition.Form != PhysicalStorageForm.SharedDocuments)
        {
            throw new InvalidOperationException(
                $"The '{unit.Identity.Value}' B7 routes require explicit shared-document physicalization.");
        }

        var logicalIndex = new LogicalIndexDeclaration(
            indexIdentity,
            [
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                new IndexField(identityPath)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var physical = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            definition.ProjectedColumns
                .Where(column => column.Path != identityPath)
                .Select(column => column.Path == ElsaRuntimeStorageManifest.WorkflowExecutionIdField
                    ? column with
                    {
                        Length = ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength
                    }
                    : column)
                .Append(new ProjectedColumnDefinition(
                    identityColumn,
                    identityPath,
                    PortablePhysicalType.String,
                    Length: ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength))
                .ToArray(),
            definition.Indexes
                .Where(index => index.LogicalName != indexIdentity)
                .Append(PhysicalIndex(
                    logicalIndex,
                    [ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, identityColumn]))
                .ToArray(),
            definition.SchemaVersion,
            definition.Evolution,
            definition.LinkedProjectionLogicalName,
            definition.LinkedKey);
        var augmented = unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                storage.ProvisioningMode,
                PhysicalStoragePolicy.Explicit(physical),
                storage.LogicalIndexes
                    .Where(index => index.Identity != indexIdentity)
                    .Append(logicalIndex)
                    .ToArray(),
                storage.BoundedQueries,
                storage.NameOverrides,
                storage.BoundedMutations)
        };

        return Physicalize(augmented);
    }

    private static StorageUnit PhysicalizeActivityExecutionInspection(StorageUnit unit)
    {
        var storage = unit.PhysicalStorage ?? throw new InvalidOperationException(
            "The activity-execution-inspection B7 route requires physical storage before route admission.");
        if (storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy { Definition: var definition } ||
            definition.Form != PhysicalStorageForm.SharedDocuments)
        {
            throw new InvalidOperationException(
                "The activity-execution-inspection B7 route requires explicit shared-document physicalization.");
        }

        var logicalIndex = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.ActivityExecutionInspectionOrderIndex,
            [
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                new IndexField(
                    ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryExecutionSequenceField,
                    IndexValueKind.Number),
                new IndexField(
                    ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryScheduledAtField,
                    IndexValueKind.DateTime),
                new IndexField(
                    ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryActivityExecutionIdField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        const string executionSequenceColumn = "inspection_execution_sequence";
        const string scheduledAtColumn = "inspection_scheduled_at";
        const string activityExecutionIdColumn = "inspection_activity_execution_id";
        string[] projectedPaths =
        [
            ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryExecutionSequenceField,
            ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryScheduledAtField,
            ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryActivityExecutionIdField
        ];
        var physical = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            definition.ProjectedColumns
                .Where(column => !projectedPaths.Contains(column.Path, StringComparer.Ordinal))
                .Select(column => column.Path is
                    ElsaRuntimeStorageManifest.WorkflowExecutionIdField or
                    ElsaRuntimeStorageManifest.ExecutionScopeIdField
                    ? column with
                    {
                        Length = ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength
                    }
                    : column)
                .Concat(
                [
                    new ProjectedColumnDefinition(
                        executionSequenceColumn,
                        ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryExecutionSequenceField,
                        PortablePhysicalType.Int64),
                    new ProjectedColumnDefinition(
                        scheduledAtColumn,
                        ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryScheduledAtField,
                        PortablePhysicalType.DateTime),
                    new ProjectedColumnDefinition(
                        activityExecutionIdColumn,
                        ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryActivityExecutionIdField,
                        PortablePhysicalType.String,
                        Length: ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength)
                ])
                .ToArray(),
            definition.Indexes
                .Where(index => index.LogicalName != logicalIndex.Identity)
                .Append(PhysicalIndex(
                    logicalIndex,
                    [
                        ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex,
                        executionSequenceColumn,
                        scheduledAtColumn,
                        activityExecutionIdColumn
                    ]))
                .ToArray(),
            definition.SchemaVersion,
            definition.Evolution,
            definition.LinkedProjectionLogicalName,
            definition.LinkedKey);
        var augmented = unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                storage.ProvisioningMode,
                PhysicalStoragePolicy.Explicit(physical),
                storage.LogicalIndexes
                    .Where(index => index.Identity != logicalIndex.Identity)
                    .Append(logicalIndex)
                    .ToArray(),
                storage.BoundedQueries,
                storage.NameOverrides,
                storage.BoundedMutations)
        };

        return Physicalize(augmented);
    }

    private static StorageUnit PhysicalizeActivityExecutionHierarchy(StorageUnit unit)
    {
        var storage = unit.PhysicalStorage ?? throw new InvalidOperationException(
            "The activity-execution-hierarchy B7 routes require physical storage before route admission.");
        if (storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy { Definition: var definition } ||
            definition.Form != PhysicalStorageForm.SharedDocuments)
        {
            throw new InvalidOperationException(
                "The activity-execution-hierarchy B7 routes require explicit shared-document physicalization.");
        }

        var latest = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.ActivityExecutionHierarchyLatestByWorkflowIndex,
            [
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                new IndexField(
                    ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField,
                    IndexValueKind.Number),
                new IndexField(
                    ElsaRuntimeStorageManifest.ActivityExecutionHierarchyActivityExecutionIdField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var pageByWorkflow = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.ActivityExecutionHierarchyPageByWorkflowIndex,
            [
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                new IndexField(
                    ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField,
                    IndexValueKind.Number),
                new IndexField(
                    ElsaRuntimeStorageManifest.ActivityExecutionHierarchyActivityExecutionIdField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var scoped = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.ActivityExecutionHierarchyByScopeAndOrderIndex,
            [
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                new IndexField(ElsaRuntimeStorageManifest.ExecutionScopeIdField),
                new IndexField(
                    ElsaRuntimeStorageManifest.ActivityExecutionHierarchyIsScopeRootField,
                    IndexValueKind.Boolean),
                new IndexField(
                    ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField,
                    IndexValueKind.Number),
                new IndexField(
                    ElsaRuntimeStorageManifest.ActivityExecutionHierarchyActivityExecutionIdField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        const string sequenceColumn = "hierarchy_execution_sequence";
        const string activityIdColumn = "hierarchy_activity_execution_id";
        const string isScopeRootColumn = "hierarchy_is_scope_root";
        string[] projectedPaths =
        [
            ElsaRuntimeStorageManifest.ActivityExecutionHierarchyIsScopeRootField,
            ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField,
            ElsaRuntimeStorageManifest.ActivityExecutionHierarchyActivityExecutionIdField
        ];
        var envelope = new DocumentEnvelopeDefinition();
        var physical = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            definition.ProjectedColumns
                .Where(column => !projectedPaths.Contains(column.Path, StringComparer.Ordinal))
                .Select(column => column.Path is
                    ElsaRuntimeStorageManifest.WorkflowExecutionIdField or
                    ElsaRuntimeStorageManifest.ExecutionScopeIdField
                    ? column with
                    {
                        Length = ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength
                    }
                    : column)
                .Concat(
                [
                    new ProjectedColumnDefinition(
                        isScopeRootColumn,
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyIsScopeRootField,
                        PortablePhysicalType.Boolean),
                    new ProjectedColumnDefinition(
                        sequenceColumn,
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField,
                        PortablePhysicalType.Int64),
                    new ProjectedColumnDefinition(
                        activityIdColumn,
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyActivityExecutionIdField,
                        PortablePhysicalType.String,
                        Length: ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength)
                ])
                .ToArray(),
            definition.Indexes
                .Where(index => index.LogicalName is not (
                    ElsaRuntimeStorageManifest.ActivityExecutionHierarchyLatestByWorkflowIndex or
                    ElsaRuntimeStorageManifest.ActivityExecutionHierarchyPageByWorkflowIndex or
                    ElsaRuntimeStorageManifest.ActivityExecutionHierarchyByScopeAndOrderIndex))
                .Concat(
                [
                    new PhysicalIndexDefinition(
                        latest.Identity,
                        [
                            new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                            new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, 1),
                            new PhysicalIndexColumnDefinition(
                                sequenceColumn,
                                2,
                                PhysicalSortDirection.Descending),
                            new PhysicalIndexColumnDefinition(
                                activityIdColumn,
                                3,
                                PhysicalSortDirection.Descending)
                        ]),
                    new PhysicalIndexDefinition(
                        pageByWorkflow.Identity,
                        [
                            new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                            new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, 1),
                            new PhysicalIndexColumnDefinition(
                                sequenceColumn,
                                2,
                                PhysicalSortDirection.Descending),
                            new PhysicalIndexColumnDefinition(
                                activityIdColumn,
                                3,
                                PhysicalSortDirection.Descending),
                            new PhysicalIndexColumnDefinition(envelope.IdLookupKeyColumn, 4)
                        ]),
                    new PhysicalIndexDefinition(
                        scoped.Identity,
                        [
                            new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                            new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, 1),
                            new PhysicalIndexColumnDefinition(ElsaRuntimeStorageManifest.ByExecutionScopeIndex, 2),
                            new PhysicalIndexColumnDefinition(isScopeRootColumn, 3),
                            new PhysicalIndexColumnDefinition(sequenceColumn, 4),
                            new PhysicalIndexColumnDefinition(activityIdColumn, 5),
                            new PhysicalIndexColumnDefinition(envelope.IdLookupKeyColumn, 6)
                        ])
                ])
                .ToArray(),
            definition.SchemaVersion,
            definition.Evolution,
            definition.LinkedProjectionLogicalName,
            definition.LinkedKey);
        var augmented = unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                storage.ProvisioningMode,
                PhysicalStoragePolicy.Explicit(physical),
                storage.LogicalIndexes
                    .Where(index => index.Identity is not (
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyLatestByWorkflowIndex or
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyPageByWorkflowIndex or
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyByScopeAndOrderIndex))
                    .Concat([latest, pageByWorkflow, scoped])
                    .ToArray(),
                storage.BoundedQueries,
                storage.NameOverrides,
                storage.BoundedMutations)
        };

        return Physicalize(augmented);
    }

    private static StorageUnit PhysicalizeWorkflowExecutableSourceReferences(StorageUnit unit) =>
        PhysicalizeEnvelopeOrderedRoutes(
            unit,
            Field(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceIdField,
                ElsaRuntimeStorageManifest.BySourceReferenceIdIndex,
                length: ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength),
            new EnvelopeOrderedRoute(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByCollectionAndId,
                [
                    Field(
                        ElsaRuntimeStorageManifest.CollectionField,
                        ElsaRuntimeStorageManifest.ByCollectionIndex,
                        length: ElsaRuntimeStorageManifest.RuntimeCollectionProjectionLength)
                ],
                UsesCursorPaging: true),
            new EnvelopeOrderedRoute(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByScopeAndId,
                [
                    Field(
                        ElsaRuntimeStorageManifest.ScopeField,
                        ElsaRuntimeStorageManifest.ByScopeIndex,
                        length: ElsaRuntimeStorageManifest.RuntimeStatusProjectionLength)
                ],
                UsesCursorPaging: true),
            new EnvelopeOrderedRoute(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByArtifactAndId,
                [
                    Field(
                        ElsaRuntimeStorageManifest.ArtifactIdField,
                        ElsaRuntimeStorageManifest.ByArtifactIndex,
                        length: ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength)
                ],
                UsesCursorPaging: true),
            new EnvelopeOrderedRoute(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceLiveByArtifactAndId,
                [
                    Field(
                        ElsaRuntimeStorageManifest.ArtifactIdField,
                        ElsaRuntimeStorageManifest.ByArtifactIndex,
                        length: ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength),
                    Field(
                        ElsaRuntimeStorageManifest.IsRetiredField,
                        ElsaRuntimeStorageManifest.ByRetiredIndex,
                        length: ElsaRuntimeStorageManifest.RuntimeStatusProjectionLength),
                    Field(
                        ElsaRuntimeStorageManifest.ExpiresAtField,
                        ElsaRuntimeStorageManifest.ByExpiresAtIndex,
                        IndexValueKind.DateTime)
                ]),
            new EnvelopeOrderedRoute(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceLiveByCollectionAndId,
                [
                    Field(
                        ElsaRuntimeStorageManifest.CollectionField,
                        ElsaRuntimeStorageManifest.ByCollectionIndex,
                        length: ElsaRuntimeStorageManifest.RuntimeCollectionProjectionLength),
                    Field(
                        ElsaRuntimeStorageManifest.IsRetiredField,
                        ElsaRuntimeStorageManifest.ByRetiredIndex,
                        length: ElsaRuntimeStorageManifest.RuntimeStatusProjectionLength),
                    Field(
                        ElsaRuntimeStorageManifest.ExpiresAtField,
                        ElsaRuntimeStorageManifest.ByExpiresAtIndex,
                        IndexValueKind.DateTime)
                ],
                UsesCursorPaging: true),
            new EnvelopeOrderedRoute(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceLiveByScopeAndId,
                [
                    Field(
                        ElsaRuntimeStorageManifest.ScopeField,
                        ElsaRuntimeStorageManifest.ByScopeIndex,
                        length: ElsaRuntimeStorageManifest.RuntimeStatusProjectionLength),
                    Field(
                        ElsaRuntimeStorageManifest.IsRetiredField,
                        ElsaRuntimeStorageManifest.ByRetiredIndex,
                        length: ElsaRuntimeStorageManifest.RuntimeStatusProjectionLength),
                    Field(
                        ElsaRuntimeStorageManifest.ExpiresAtField,
                        ElsaRuntimeStorageManifest.ByExpiresAtIndex,
                        IndexValueKind.DateTime)
                ],
                UsesCursorPaging: true),
            new EnvelopeOrderedRoute(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByExpiryAndId,
                [
                    Field(
                        ElsaRuntimeStorageManifest.ExpiresAtField,
                        ElsaRuntimeStorageManifest.ByExpiresAtIndex,
                        IndexValueKind.DateTime)
                ]),
            new EnvelopeOrderedRoute(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByRetiredAndId,
                [
                    Field(
                        ElsaRuntimeStorageManifest.IsRetiredField,
                        ElsaRuntimeStorageManifest.ByRetiredIndex,
                        length: ElsaRuntimeStorageManifest.RuntimeStatusProjectionLength),
                    Field(
                        ElsaRuntimeStorageManifest.ExpiresAtField,
                        ElsaRuntimeStorageManifest.ByExpiresAtIndex,
                        IndexValueKind.DateTime)
                ]));

    private static StorageUnit PhysicalizeEnvelopeOrderedRoutes(
        StorageUnit unit,
        EnvelopeOrderedField identity,
        params EnvelopeOrderedRoute[] routes)
    {
        var storage = unit.PhysicalStorage ?? throw new InvalidOperationException(
            $"The '{unit.Identity.Value}' B7 routes require physical storage before route admission.");
        if (storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy { Definition: var definition } ||
            definition.Form != PhysicalStorageForm.SharedDocuments)
        {
            throw new InvalidOperationException(
                $"The '{unit.Identity.Value}' B7 routes require explicit shared-document physicalization.");
        }

        var logicalIndexes = routes.Select(route =>
            new LogicalIndexDeclaration(
                route.IndexIdentity,
                [
                    .. route.Fields.Select(field => new IndexField(field.Path, field.Kind)),
                    new IndexField(identity.Path, identity.Kind)
                ],
                IndexValueKind.Keyword,
                isUnique: false,
                MissingValueBehavior.Excluded)).ToArray();
        var routeIdentities = routes
            .Select(route => route.IndexIdentity)
            .ToHashSet(StringComparer.Ordinal);
        var envelope = new DocumentEnvelopeDefinition();
        var routeFieldsByPath = routes
            .SelectMany(route => route.Fields)
            .GroupBy(field => field.Path, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var existingProjectedPaths = definition.ProjectedColumns
            .Select(column => column.Path)
            .ToHashSet(StringComparer.Ordinal);
        var projected = definition.ProjectedColumns
            .Where(column => column.Path != identity.Path)
            .Concat(routeFieldsByPath.Values
                .Where(field => !existingProjectedPaths.Contains(field.Path))
                .Select(Projected))
            .Append(new ProjectedColumnDefinition(
                identity.ProjectedColumn,
                identity.Path,
                PortablePhysicalType.String,
                Length: identity.Length))
            .ToArray();
        var physical = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            projected,
            definition.Indexes
                .Where(index => !routeIdentities.Contains(index.LogicalName))
                .Concat(routes.Select(route =>
                    new PhysicalIndexDefinition(
                        route.IndexIdentity,
                        [
                            new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                            .. route.Fields.Select((field, index) =>
                                new PhysicalIndexColumnDefinition(
                                    field.ProjectedColumn,
                                    index + 1)),
                            new PhysicalIndexColumnDefinition(
                                identity.ProjectedColumn,
                                route.Fields.Count + 1),
                            .. CursorTieBreakColumns(route, envelope)
                        ])))
                .ToArray(),
            definition.SchemaVersion,
            definition.Evolution,
            definition.LinkedProjectionLogicalName,
            definition.LinkedKey);
        var augmented = unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                storage.ProvisioningMode,
                PhysicalStoragePolicy.Explicit(physical),
                storage.LogicalIndexes
                    .Where(index => !routeIdentities.Contains(index.Identity))
                    .Concat(logicalIndexes)
                    .ToArray(),
                storage.BoundedQueries,
                storage.NameOverrides,
                storage.BoundedMutations)
        };

        return Physicalize(augmented);
    }

    private static IReadOnlyList<PhysicalIndexColumnDefinition> CursorTieBreakColumns(
        EnvelopeOrderedRoute route,
        DocumentEnvelopeDefinition envelope) =>
        route.UsesCursorPaging
            ?
            [
                new PhysicalIndexColumnDefinition(
                    envelope.IdLookupKeyColumn,
                    route.Fields.Count + 2)
            ]
            : [];

    private static EnvelopeOrderedField Field(
        string path,
        string projectedColumn,
        IndexValueKind kind = IndexValueKind.Keyword,
        int? length = null) =>
        new(path, projectedColumn, kind, length);

    private static ProjectedColumnDefinition Projected(EnvelopeOrderedField field) => new(
        field.ProjectedColumn,
        field.Path,
        field.Kind switch
        {
            IndexValueKind.Boolean => PortablePhysicalType.Boolean,
            IndexValueKind.DateTime => PortablePhysicalType.DateTime,
            IndexValueKind.Number => PortablePhysicalType.Int64,
            _ => PortablePhysicalType.String
        },
        Length: field.Kind == IndexValueKind.Keyword
            ? field.Length ?? LegacyGroundworkStorageManifestPhysicalizer.LegacyStringProjectionLength
            : null);

    private static BoundedQueryDeclaration ToBoundedQuery(ElsaGroundworkPhysicalQueryRoute route)
    {
        var predicates = route.Predicates
            .GroupBy(predicate => predicate.Path, StringComparer.Ordinal)
            .Select(group => new BoundedQueryPredicateField(
                group.Key,
                group.SelectMany(predicate => predicate.Operations).ToHashSet()))
            .ToArray();

        return new BoundedQueryDeclaration(
            route.Identity,
            route.IndexIdentity,
            predicates.SelectMany(predicate => predicate.Operations).ToHashSet(),
            route.OrderingFields.Count == 0
                ? QuerySortSupport.None
                : route.OrderingDirections?.Any(direction => direction == PhysicalSortDirection.Descending) == true
                    ? QuerySortSupport.Descending
                    : QuerySortSupport.Ascending,
            route.Continuation switch
            {
                ElsaGroundworkQueryContinuation.Cursor => QueryPagingSupport.Cursor,
                ElsaGroundworkQueryContinuation.Offset => QueryPagingSupport.Offset,
                _ => QueryPagingSupport.None
            },
            BoundedQueryExecutionClass.ScaleBearing,
            supportsTotalCount: true,
            sortFields: route.OrderingFields
                .Select((path, index) => new BoundedQuerySortField(
                    path,
                    route.OrderingDirections?[index] ?? PhysicalSortDirection.Ascending))
                .ToArray(),
            predicateFields: predicates,
            latestPerKeyPath: route.LatestPerKeyPath);
    }

    private static ElsaGroundworkPhysicalQueryRoute WorkflowExecutionHistoryRoute() =>
        BoundedOrderedDirections(
            ElsaRuntimeStorageManifest.PageWorkflowExecutionsQuery,
            ElsaRuntimeStorageManifest.WorkflowExecutionHistoryOrderIndex,
            ElsaGroundworkQueryContinuation.Cursor,
            [
                new ElsaGroundworkQueryOrder(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField,
                    PhysicalSortDirection.Descending),
                new ElsaGroundworkQueryOrder(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField,
                    PhysicalSortDirection.Ascending)
            ],
            Equal(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryTenantIdField),
            Equal(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryDefinitionIdField),
            Equal(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryStatusField),
            Equal(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryRunKindField),
            Equal(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryCorrelationIdField),
            Equal(PhysicalDocumentFieldPaths.Id),
            Equal(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField),
            GreaterThanOrEqual(ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField),
            LessThanOrEqual(ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField));

    private static ElsaGroundworkPhysicalQueryRoute FaultedWorkflowExecutionAttentionRoute() =>
        BoundedOrderedDirections(
            ElsaRuntimeStorageManifest.PageFaultedWorkflowExecutionsForAttentionQuery,
            ElsaRuntimeStorageManifest.WorkflowExecutionFaultedAttentionOrderIndex,
            ElsaGroundworkQueryContinuation.Cursor,
            [
                new ElsaGroundworkQueryOrder(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField,
                    PhysicalSortDirection.Descending),
                new ElsaGroundworkQueryOrder(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField,
                    PhysicalSortDirection.Ascending)
            ],
            Equal(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryTenantIdField),
            Equal(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryStatusField));

    private static ElsaGroundworkQueryRoute Primary(
        string coverageRow,
        string queryShape,
        string documentKind,
        string primaryReadIdentity) => new(
        $"{coverageRow}:{queryShape}",
        coverageRow,
        queryShape,
        documentKind,
        ElsaGroundworkQueryRouteKind.PrimaryIdentityRead,
        primaryReadIdentity,
        1,
        ElsaGroundworkQueryContinuation.None,
        ElsaGroundworkQueryResultOperation.First,
        [],
        []);

    private static ElsaGroundworkQueryRoute Route(
        string coverageRow,
        string queryShape,
        string documentKind,
        ElsaGroundworkQueryResultOperation resultOperation,
        params ElsaGroundworkPhysicalQueryRoute[] routes) => new(
        $"{coverageRow}:{queryShape}",
        coverageRow,
        queryShape,
        documentKind,
        ElsaGroundworkQueryRouteKind.BoundedRoute,
        null,
        MaximumResultCount,
        routes.Any(route => route.Continuation == ElsaGroundworkQueryContinuation.Cursor)
            ? ElsaGroundworkQueryContinuation.Cursor
            : routes.Any(route => route.Continuation == ElsaGroundworkQueryContinuation.Offset)
                ? ElsaGroundworkQueryContinuation.Offset
                : ElsaGroundworkQueryContinuation.None,
        resultOperation,
        routes.SelectMany(route => route.Predicates.Select(predicate => predicate.Path).Concat(route.OrderingFields)).Distinct(StringComparer.Ordinal).ToArray(),
        routes);

    private static ElsaGroundworkPhysicalQueryRoute Bounded(
        string identity,
        string indexIdentity,
        params ElsaGroundworkQueryPredicate[] predicates) =>
        Bounded(identity, indexIdentity, ElsaGroundworkQueryContinuation.None, predicates);

    private static ElsaGroundworkPhysicalQueryRoute Bounded(
        string identity,
        string indexIdentity,
        ElsaGroundworkQueryContinuation continuation,
        params ElsaGroundworkQueryPredicate[] predicates) => new(identity, indexIdentity, continuation, [], predicates);

    private static ElsaGroundworkPhysicalQueryRoute BoundedOrdered(
        string identity,
        string indexIdentity,
        ElsaGroundworkQueryContinuation continuation,
        IReadOnlyList<string> orderingFields,
        params ElsaGroundworkQueryPredicate[] predicates) =>
        new(identity, indexIdentity, continuation, orderingFields, predicates);

    private static ElsaGroundworkPhysicalQueryRoute BoundedOrdered(
        string identity,
        string indexIdentity,
        ElsaGroundworkQueryContinuation continuation,
        IReadOnlyList<string> orderingFields,
        string latestPerKeyPath,
        params ElsaGroundworkQueryPredicate[] predicates) =>
        new(
            identity,
            indexIdentity,
            continuation,
            orderingFields,
            predicates,
            LatestPerKeyPath: latestPerKeyPath);

    private static ElsaGroundworkPhysicalQueryRoute BoundedOrderedDirections(
        string identity,
        string indexIdentity,
        ElsaGroundworkQueryContinuation continuation,
        IReadOnlyList<ElsaGroundworkQueryOrder> ordering,
        params ElsaGroundworkQueryPredicate[] predicates) =>
        new(
            identity,
            indexIdentity,
            continuation,
            ordering.Select(item => item.Path).ToArray(),
            predicates,
            ordering.Select(item => item.Direction).ToArray());

    private static PhysicalIndexDefinition PhysicalIndex(
        LogicalIndexDeclaration index,
        IReadOnlyList<string> projectedColumns) => new(
        index.Identity,
        [
            new PhysicalIndexColumnDefinition(new DocumentEnvelopeDefinition().StorageScopeColumn, 0),
            .. projectedColumns.Select((column, position) =>
                new PhysicalIndexColumnDefinition(column, position + 1)),
            new PhysicalIndexColumnDefinition(
                new DocumentEnvelopeDefinition().IdLookupKeyColumn,
                projectedColumns.Count + 1)
        ]);

    private static ElsaGroundworkQueryPredicate Equal(string path) =>
        new(path, new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal });

    private static ElsaGroundworkQueryPredicate GreaterThanOrEqual(string path) =>
        new(path, new HashSet<PortableQueryOperation> { PortableQueryOperation.GreaterThanOrEqual });

    private static ElsaGroundworkQueryPredicate GreaterThan(string path) =>
        new(path, new HashSet<PortableQueryOperation> { PortableQueryOperation.GreaterThan });

    private static ElsaGroundworkQueryPredicate LessThanOrEqual(string path) =>
        new(path, new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual });

    private static ElsaGroundworkQueryResultOperation Documents() => ElsaGroundworkQueryResultOperation.Documents;

    private static ElsaGroundworkQueryResultOperation Projection() => ElsaGroundworkQueryResultOperation.Projection;
}

public enum ElsaGroundworkQueryRouteKind
{
    PrimaryIdentityRead,
    BoundedRoute
}

public enum ElsaGroundworkQueryContinuation
{
    None,
    Cursor,
    Offset
}

public enum ElsaGroundworkQueryResultOperation
{
    First,
    Documents,
    Any,
    Projection
}

public sealed record ElsaGroundworkQueryRoute(
    string Key,
    string CoverageRow,
    string QueryShape,
    string DocumentKind,
    ElsaGroundworkQueryRouteKind Kind,
    string? PrimaryReadIdentity,
    int MaximumResultCount,
    ElsaGroundworkQueryContinuation Continuation,
    ElsaGroundworkQueryResultOperation ResultOperation,
    IReadOnlyList<string> RequiredPhysicalFields,
    IReadOnlyList<ElsaGroundworkPhysicalQueryRoute> PhysicalRoutes);

public sealed record ElsaGroundworkPhysicalQueryRoute(
    string Identity,
    string IndexIdentity,
    ElsaGroundworkQueryContinuation Continuation,
    IReadOnlyList<string> OrderingFields,
    IReadOnlyList<ElsaGroundworkQueryPredicate> Predicates,
    IReadOnlyList<PhysicalSortDirection>? OrderingDirections = null,
    string? LatestPerKeyPath = null);

public sealed record ElsaGroundworkQueryOrder(
    string Path,
    PhysicalSortDirection Direction);

internal sealed record EnvelopeOrderedRoute(
    string IndexIdentity,
    IReadOnlyList<EnvelopeOrderedField> Fields,
    bool UsesCursorPaging = false);

internal sealed record EnvelopeOrderedField(
    string Path,
    string ProjectedColumn,
    IndexValueKind Kind,
    int? Length);

public sealed record ElsaGroundworkQueryPredicate(
    string Path,
    IReadOnlySet<PortableQueryOperation> Operations);
