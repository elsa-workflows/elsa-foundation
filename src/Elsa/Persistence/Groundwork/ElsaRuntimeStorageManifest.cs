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
    public const string SchemaVersion = "1.0.0";

    // Shared index identities and field names. Index identities only need to be unique within a unit,
    // so the same strings are reused across units that expose the same logical access pattern.
    public const string ByWorkflowExecutionIndex = "by-workflow-execution";
    public const string ByCollectionIndex = "by-collection";
    public const string WorkflowExecutionIdField = "workflowExecutionId";
    public const string CollectionField = "collection";

    public const string BookmarkStateDocumentKind = "bookmarkState";

    /// <summary>Index used by <c>IBookmarkStateStore.ListAsync(workflowExecutionId)</c>.</summary>
    public const string BookmarkStateByWorkflowExecution = ByWorkflowExecutionIndex;

    public const string WorkflowExecutableDocumentKind = "workflowExecutable";

    /// <summary>Index used by <c>IWorkflowExecutableStore.ListAsync()</c> to enumerate every executable.</summary>
    public const string WorkflowExecutableByCollection = ByCollectionIndex;

    /// <summary>
    /// Constant partition value stamped on every workflow executable document so the unfiltered
    /// <c>ListAsync()</c> can be served through the declared-index equality query contract that every
    /// provider supports, rather than relying on a provider-specific "scan all" capability.
    /// </summary>
    public const string WorkflowExecutableCollection = "workflowExecutable";

    public const string ActivityExecutionStateDocumentKind = "activityExecutionState";
    public const string ActivityExecutionInspectionDocumentKind = "activityExecutionInspection";
    public const string WorkflowExecutionStateDocumentKind = "workflowExecutionState";
    public const string DurableValueStateDocumentKind = "durableValueState";
    public const string SchedulerStateDocumentKind = "schedulerState";
    public const string OperationalStateDocumentKind = "operationalState";
    public const string ControlPlaneStateDocumentKind = "controlPlaneState";
    public const string IncidentStateDocumentKind = "incidentState";

    // Durable idempotency ledger for the checkpoint writer. A marker document keyed by CommitId records
    // that a checkpoint commit has been fully applied, so an at-least-once redelivery of the same commit
    // is skipped. This survives process restarts, unlike the in-memory writer's in-process dedup set.
    public const string CheckpointCommitDocumentKind = "checkpointCommit";

    public const string PostCommitOutboxDocumentKind = "postCommitOutbox";

    // Durable scheduler work queue. Each queued work item is a document so the queue survives process
    // restarts; the by-collection partition supports the system-wide pending-executions sweep.
    public const string SchedulerWorkItemDocumentKind = "schedulerWorkItem";

    public static StorageManifest Create() => new(
        new StorageManifestIdentity("elsa-workflows-runtime"),
        new StorageManifestOwner("elsa.workflows.runtime"),
        new StorageManifestVersion(SchemaVersion),
        [
            Unit(
                BookmarkStateDocumentKind,
                "Bookmark state",
                [Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField)],
                [Query("list-by-workflow-execution", ByWorkflowExecutionIndex)]),
            Unit(
                WorkflowExecutableDocumentKind,
                "Workflow executable",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query("list-all", ByCollectionIndex)]),
            Unit(
                ActivityExecutionStateDocumentKind,
                "Activity execution state",
                [Keyword(ByWorkflowExecutionIndex, WorkflowExecutionIdField)],
                [Query("list-by-workflow-execution", ByWorkflowExecutionIndex)]),
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
                OperationalStateDocumentKind,
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
                ControlPlaneStateDocumentKind,
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
                ])
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
        TenancyPolicy.None,
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
