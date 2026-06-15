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

    public const string BookmarkStateDocumentKind = "bookmarkState";

    /// <summary>Index used by <c>IBookmarkStateStore.ListAsync(workflowExecutionId)</c>.</summary>
    public const string BookmarkStateByWorkflowExecution = "by-workflow-execution";

    public const string WorkflowExecutionIdField = "workflowExecutionId";

    public static StorageManifest Create() => new(
        new StorageManifestIdentity("elsa-workflows-runtime"),
        new StorageManifestOwner("elsa.workflows.runtime"),
        new StorageManifestVersion(SchemaVersion),
        [
            new StorageUnit(
                new StorageUnitIdentity(BookmarkStateDocumentKind),
                "Bookmark state",
                StorageIntent.PortableDocument(),
                LifecyclePolicy.Mutable,
                IdentityPolicy.StringId(),
                TenancyPolicy.None,
                ConcurrencyPolicy.Optimistic(),
                SerializationPolicy.Json(),
                [
                    Keyword(BookmarkStateByWorkflowExecution, WorkflowExecutionIdField)
                ],
                [
                    new PortableQueryDeclaration(
                        "list-by-workflow-execution",
                        BookmarkStateByWorkflowExecution,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                        QuerySortSupport.None,
                        QueryPagingSupport.Offset)
                ],
                PhysicalizationPolicy.Portable)
        ],
        new HashSet<string> { "schema-history", "optimistic-concurrency" },
        []);

    private static IndexDeclaration Keyword(string identity, string field) => new(
        identity,
        [new IndexField(field)],
        IndexValueKind.Keyword,
        false,
        true,
        MissingValueBehavior.Excluded,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal });
}
