using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;

namespace Elsa.Persistence.Groundwork;

/// <summary>
/// Provider-neutral Groundwork storage manifest describing the runtime persistence document kinds
/// backed by the Groundwork bridge. The manifest is shared by every provider; a host selects the
/// concrete provider (SQLite, SQL Server, PostgreSQL, MongoDB) without changing this description.
/// </summary>
public static class ElsaRuntimeStorageManifest
{
    // FROZEN legacy stamp. This is NOT a migration knob: ElsaRuntimeDocumentVersions.Parse recognizes only a
    // positive integer or this exact "1.0.0" string (which it maps to version 1), and documents/tests stamp
    // with this value — changing it makes Parse reject every kind. Per-kind schema versions live separately in
    // ElsaRuntimeDocumentVersions.Current. Adding an index (like #514's by-parent-activity-execution, or the
    // pre-existing bookmarkState by-stimulus) does NOT bump this: Groundwork's added-index backfill (Condition 7)
    // triggers on the physicalized index-set change, not on this string. See docs/serialization.md.
    public const string SchemaVersion = "1.0.0";

    // Shared index identities and field names. Index identities only need to be unique within a unit,
    // so the same strings are reused across units that expose the same logical access pattern.
    public const string ByWorkflowExecutionIndex = "by-workflow-execution";
    public const string ByCollectionIndex = "by-collection";
    public const string ByStimulusIndex = "by-stimulus";
    public const string ByArtifactIndex = "by-artifact";
    public const string ByParentActivityExecutionIndex = "by-parent-activity-execution";
    public const string WorkflowExecutionIdField = "workflowExecutionId";
    public const string CollectionField = "collection";
    public const string StimulusHashField = "stimulusHash";
    public const string ArtifactIdField = "artifactId";
    // Nested dot-path into the persisted activity-execution document: the parent id already lives under
    // the document's "state" envelope, so indexing this path adds an index over an EXISTING serialized
    // field without changing the document shape. Groundwork index fields are dot-paths resolved by walking
    // nested JSON (relational RelationalPhysicalizationValues.TryGetPropertyPath, Mongo content.<path> BSON key).
    public const string ParentActivityExecutionIdField = "state.parentActivityExecutionId";

    public const string BookmarkStateDocumentKind = "bookmarkState";

    /// <summary>Index used by <c>IBookmarkStateStore.ListAsync(workflowExecutionId)</c>.</summary>
    public const string BookmarkStateByWorkflowExecution = ByWorkflowExecutionIndex;

    /// <summary>
    /// Cross-execution index used by <c>IBookmarkStimulusIndex.ListByStimulusAsync</c> (W7, E3-5) so a
    /// single stimulus can fan in to bookmarks waiting in any workflow execution. Keyed by stimulus hash
    /// alone; the caller post-filters by stimulus type (the hash is already type-derived in practice).
    /// </summary>
    public const string BookmarkStateByStimulus = ByStimulusIndex;

    public const string WorkflowExecutableDocumentKind = "workflowExecutable";

    /// <summary>Index used by <c>IWorkflowExecutableStore.ListAsync()</c> to enumerate every executable.</summary>
    public const string WorkflowExecutableByCollection = ByCollectionIndex;

    /// <summary>
    /// Constant partition value stamped on every workflow executable document so the unfiltered
    /// <c>ListAsync()</c> can be served through the declared-index equality query contract that every
    /// provider supports, rather than relying on a provider-specific "scan all" capability.
    /// </summary>
    public const string WorkflowExecutableCollection = "workflowExecutable";

    // Per-publish source references into the content-addressed artifact store (ADR 0038/0039/0040). One document
    // per reference; carries source identity, scope/expiry, retirement facts and the embedded layout sidecar.
    public const string WorkflowExecutableSourceReferenceDocumentKind = "workflowExecutableSourceReference";

    /// <summary>Index used by <c>IWorkflowExecutableSourceReferenceStore.ListAsync()</c> to enumerate every reference.</summary>
    public const string WorkflowExecutableSourceReferenceByCollection = ByCollectionIndex;

    /// <summary>Index used by <c>IWorkflowExecutableSourceReferenceStore.ListByArtifactAsync</c> and the GC unreferenced-artifact sweep.</summary>
    public const string WorkflowExecutableSourceReferenceByArtifact = ByArtifactIndex;

    /// <summary>Constant partition value stamped on every source-reference document so the unfiltered list/expiry sweep can use a keyword equality index.</summary>
    public const string WorkflowExecutableSourceReferenceCollection = "workflowExecutableSourceReference";

    public const string ActivityExecutionStateDocumentKind = "activityExecutionState";

    /// <summary>
    /// Parent-scoped index used by <c>IActivityExecutionStateStore.ListByParentAsync</c> (#514/#413 item 3) so a
    /// composite (e.g. a Parallel fork/join) can read only the activity-execution states directly parented by it,
    /// instead of loading every activity-execution state in the workflow and filtering in memory. Keyed by the
    /// already-persisted nested <c>state.parentActivityExecutionId</c> field; the store post-filters the result by
    /// workflow execution id (parent ids are activity-execution ids, but the store honours the full (wf, parent)
    /// contract rather than relying on their global uniqueness).
    /// </summary>
    public const string ActivityExecutionStateByParent = ByParentActivityExecutionIndex;
    public const string ActivityExecutionInspectionDocumentKind = "activityExecutionInspection";
    public const string WorkflowExecutionStateDocumentKind = "workflowExecutionState";
    public const string DurableValueStateDocumentKind = "durableValueState";
    public const string SchedulerStateDocumentKind = "schedulerState";
    // Persisted wire identifiers — the string values predate the W14 type renames
    // (ExecutionLivenessState was OperationalState; WorkflowHoldState was ControlPlaneState).
    // Do not change the literal values: they are the durable Groundwork document-kind discriminators.
    public const string ExecutionLivenessStateDocumentKind = "operationalState";
    public const string WorkflowHoldStateDocumentKind = "controlPlaneState";
    public const string IncidentStateDocumentKind = "incidentState";

    // Durable idempotency ledger for the checkpoint writer. A marker document keyed by CommitId records
    // that a checkpoint commit has been fully applied, so an at-least-once redelivery of the same commit
    // is skipped. This survives process restarts, unlike the in-memory writer's in-process dedup set.
    public const string CheckpointCommitDocumentKind = "checkpointCommit";

    public const string PostCommitOutboxDocumentKind = "postCommitOutbox";

    // Durable scheduler work queue. Each queued work item is a document so the queue survives process
    // restarts; the by-collection partition supports the system-wide pending-executions sweep.
    public const string SchedulerWorkItemDocumentKind = "schedulerWorkItem";

    // Durable timer store. Each pending timer is a document so timers survive process restarts; the
    // by-collection partition serves the due-timer sweep through an equality index (Groundwork is
    // equality-only, so due-time filtering/ordering happens in memory — see GroundworkDurableTimerStore).
    public const string DurableTimerDocumentKind = "durableTimer";

    // Durable trigger index over PUBLISHED artifacts (W7, E3-1). Each start-trigger activity in a
    // published executable becomes one document, so an external stimulus with no execution id can be
    // routed to start a new workflow instance. Indexed by stimulus hash (cross-artifact router lookup)
    // and by artifact id (replace-on-republish).
    public const string WorkflowTriggerBindingDocumentKind = "workflowTriggerBinding";

    /// <summary>Durable prepared/active marker for a publication-owned Runtime serving projection.</summary>
    public const string PublicationProjectionStateDocumentKind = "publicationProjectionState";

    /// <summary>
    /// Cross-artifact index used by <c>IWorkflowTriggerBindingStore.ListByStimulusAsync</c> so a single
    /// stimulus can start instances of any workflow that triggers on it. Keyed by stimulus hash alone; the
    /// caller post-filters by stimulus type (the hash is type-derived in practice).
    /// </summary>
    public const string WorkflowTriggerBindingByStimulus = ByStimulusIndex;

    /// <summary>Index used by <c>IWorkflowTriggerBindingStore.ListByArtifactAsync</c> and the republish replace path.</summary>
    public const string WorkflowTriggerBindingByArtifact = ByArtifactIndex;

    // Durable recurring-trigger schedule store (W16). Each Timer/Cron start trigger in a published artifact
    // becomes one schedule document with no execution id, so the recurring-trigger pump can start a NEW
    // instance on each occurrence across process restarts. The by-collection partition serves the due-schedule
    // sweep through an equality index (Groundwork is equality-only, so next-occurrence filtering/ordering
    // happens in memory — see GroundworkRecurringTriggerScheduleStore); the by-artifact index serves
    // replace-on-republish.
    public const string RecurringTriggerScheduleDocumentKind = "recurringTriggerSchedule";

    /// <summary>Index used by the recurring-trigger pump's due-schedule sweep (constant partition).</summary>
    public const string RecurringTriggerScheduleByCollection = ByCollectionIndex;

    /// <summary>Index used by the recurring-schedule replace-on-republish delete path.</summary>
    public const string RecurringTriggerScheduleByArtifact = ByArtifactIndex;

    public static StorageManifest Create() => new(
        new StorageManifestIdentity("elsa-workflows-runtime"),
        new StorageManifestOwner("elsa.workflows.runtime"),
        new StorageManifestVersion(SchemaVersion),
        [
            Unit(
                BookmarkStateDocumentKind,
                "Bookmark state",
                [
                    Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField),
                    Keyword(ByStimulusIndex, StimulusHashField)
                ],
                [
                    Query("list-by-workflow-execution", ByWorkflowExecutionIndex),
                    Query("list-by-stimulus", ByStimulusIndex)
                ]),
            Unit(
                WorkflowExecutableDocumentKind,
                "Workflow executable",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query("list-all", ByCollectionIndex)]),
            Unit(
                WorkflowExecutableSourceReferenceDocumentKind,
                "Workflow executable source reference",
                [
                    Keyword(ByCollectionIndex, CollectionField),
                    Keyword(ByArtifactIndex, ArtifactIdField)
                ],
                [
                    Query("list-all", ByCollectionIndex),
                    Query("list-by-artifact", ByArtifactIndex)
                ]),
            Unit(
                ActivityExecutionStateDocumentKind,
                "Activity execution state",
                [
                    Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField),
                    Keyword(ByParentActivityExecutionIndex, ParentActivityExecutionIdField)
                ],
                [
                    Query("list-by-workflow-execution", ByWorkflowExecutionIndex),
                    Query("list-by-parent-activity-execution", ByParentActivityExecutionIndex)
                ]),
            Unit(
                ActivityExecutionInspectionDocumentKind,
                "Activity execution inspection projection",
                [Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField)],
                [Query("list-by-workflow-execution", ByWorkflowExecutionIndex)]),
            Unit(
                WorkflowExecutionStateDocumentKind,
                "Workflow execution state",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query("list-all", ByCollectionIndex)]),
            Unit(
                DurableValueStateDocumentKind,
                "Durable value state",
                [Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField)],
                [Query("list-by-workflow-execution", ByWorkflowExecutionIndex)]),
            Unit(
                SchedulerStateDocumentKind,
                "Scheduler state",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query("list-all", ByCollectionIndex)]),
            Unit(
                ExecutionLivenessStateDocumentKind,
                "Operational state",
                [
                    Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField),
                    Keyword(ByCollectionIndex, CollectionField)
                ],
                [
                    Query("list-by-workflow-execution", ByWorkflowExecutionIndex),
                    Query("list-all", ByCollectionIndex)
                ]),
            Unit(
                WorkflowHoldStateDocumentKind,
                "Control plane state",
                [
                    Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField),
                    Keyword(ByCollectionIndex, CollectionField)
                ],
                [
                    Query("list-by-workflow-execution", ByWorkflowExecutionIndex),
                    Query("list-all", ByCollectionIndex)
                ]),
            Unit(
                IncidentStateDocumentKind,
                "Incident state",
                [Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField)],
                [Query("list-by-workflow-execution", ByWorkflowExecutionIndex)]),
            Unit(
                CheckpointCommitDocumentKind,
                "Checkpoint commit ledger",
                [],
                []),
            Unit(
                PostCommitOutboxDocumentKind,
                "Post-commit outbox",
                [
                    Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField),
                    Keyword(ByCollectionIndex, CollectionField)
                ],
                [
                    Query("list-by-workflow-execution", ByWorkflowExecutionIndex),
                    Query("list-all", ByCollectionIndex)
                ]),
            Unit(
                SchedulerWorkItemDocumentKind,
                "Scheduler work queue item",
                [
                    Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField),
                    Keyword(ByCollectionIndex, CollectionField)
                ],
                [
                    Query("list-by-workflow-execution", ByWorkflowExecutionIndex),
                    Query("list-all", ByCollectionIndex)
                ]),
            Unit(
                DurableTimerDocumentKind,
                "Durable timer",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query("list-all", ByCollectionIndex)]),
            Unit(
                WorkflowTriggerBindingDocumentKind,
                "Workflow trigger binding",
                [
                    Keyword(ByStimulusIndex, StimulusHashField),
                    Keyword(ByArtifactIndex, ArtifactIdField)
                ],
                [
                    Query("list-by-stimulus", ByStimulusIndex),
                    Query("list-by-artifact", ByArtifactIndex)
                ]),
            Unit(
                RecurringTriggerScheduleDocumentKind,
                "Recurring trigger schedule",
                [
                    Keyword(ByCollectionIndex, CollectionField),
                    Keyword(ByArtifactIndex, ArtifactIdField)
                ],
                [
                    Query("list-all", ByCollectionIndex),
                    Query("list-by-artifact", ByArtifactIndex)
                ]),
            Unit(
                PublicationProjectionStateDocumentKind,
                "Publication projection state",
                [],
                [])
        ],
        new HashSet<string> { "schema-history", "optimistic-concurrency" },
        []);

    private static StorageUnit Unit(
        string documentKind,
        string label,
        IndexDeclaration[] indexes,
        PortableQueryDeclaration[] queries) => new(
        new StorageUnitIdentity(documentKind),
        label,
        StorageIntent.PortableDocument(),
        LifecyclePolicy.Mutable,
        IdentityPolicy.StringId(),
        TenancyPolicy.Global,
        ConcurrencyPolicy.Optimistic(),
        SerializationPolicy.Json(),
        indexes,
        queries,
        PhysicalizationPolicy.Portable);

    private static PortableQueryDeclaration Query(string name, string indexName) => new(
        name,
        indexName,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        QuerySortSupport.None,
        QueryPagingSupport.Offset);

    private static IndexDeclaration Keyword(string identity, string field) => new(
        identity,
        [new IndexField(field)],
        IndexValueKind.Keyword,
        false,
        true,
        MissingValueBehavior.Excluded,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal });
}
