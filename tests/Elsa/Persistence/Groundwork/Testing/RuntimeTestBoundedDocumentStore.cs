using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Adapts standalone provider fixtures to Runtime's admitted bounded-query contract. Production hosts obtain
/// the equivalent route-bound query runtime from their provider initializer.
/// </summary>
public sealed class RuntimeTestBoundedDocumentStore(IDocumentStore documents) : IBoundedDocumentStore
{
    public async Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
    {
        var (index, path) = query.QueryIdentity switch
        {
            ElsaRuntimeStorageManifest.ListAllQuery =>
                (ElsaRuntimeStorageManifest.ByCollectionIndex, ElsaRuntimeStorageManifest.CollectionField),
            ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery =>
                (ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
            ElsaRuntimeStorageManifest.ListByArtifactQuery =>
                (ElsaRuntimeStorageManifest.ByArtifactIndex, ElsaRuntimeStorageManifest.ArtifactIdField),
            ElsaRuntimeStorageManifest.ListByParentActivityExecutionQuery =>
                (ElsaRuntimeStorageManifest.ByParentActivityExecutionIndex, ElsaRuntimeStorageManifest.ParentActivityExecutionIdField),
            ElsaRuntimeStorageManifest.ListBookmarksByStimulusQuery =>
                (ElsaRuntimeStorageManifest.ByStimulusIndex, ElsaRuntimeStorageManifest.StimulusHashField),
            ElsaRuntimeStorageManifest.ListBookmarksByStimulusTypeQuery =>
                (ElsaRuntimeStorageManifest.ByStimulusTypeIndex, ElsaRuntimeStorageManifest.StimulusTypeField),
            ElsaRuntimeStorageManifest.FindExecutableActivityTemplateByHashQuery =>
                (ElsaRuntimeStorageManifest.ByTemplateHashIndex, ElsaRuntimeStorageManifest.TemplateHashField),
            ElsaRuntimeStorageManifest.ListWorkflowDispatchesByParentQuery =>
                (ElsaRuntimeStorageManifest.ByParentWorkflowExecutionIndex, ElsaRuntimeStorageManifest.ParentWorkflowExecutionIdField),
            ElsaRuntimeStorageManifest.ListWorkflowDispatchesByChildQuery =>
                (ElsaRuntimeStorageManifest.ByChildWorkflowExecutionIndex, ElsaRuntimeStorageManifest.ChildWorkflowExecutionIdField),
            ElsaRuntimeStorageManifest.ListWorkflowDispatchesByStatusQuery =>
                (ElsaRuntimeStorageManifest.ByStatusIndex, ElsaRuntimeStorageManifest.StatusField),
            ElsaRuntimeStorageManifest.ListWorkflowDispatchesByTestScopeQuery =>
                (ElsaRuntimeStorageManifest.ByTestScopeIndex, ElsaRuntimeStorageManifest.TestScopeIdField),
            "list-by-execution-scope" =>
                (ElsaRuntimeStorageManifest.ByExecutionScopeIndex, ElsaRuntimeStorageManifest.ExecutionScopeIdField),
            ElsaRuntimeStorageManifest.ListTriggerBindingsByPublicationQuery =>
                (ElsaRuntimeStorageManifest.ByPublicationIndex, ElsaRuntimeStorageManifest.PublicationIdField),
            _ => throw new InvalidOperationException($"Undeclared Runtime test query '{query.QueryIdentity}'.")
        };
        var clause = query.Clauses.Count == 1
            ? query.Clauses[0]
            : throw new InvalidOperationException($"Runtime test query '{query.QueryIdentity}' must have one clause.");
        var comparison = clause.Comparisons.Count == 1
            ? clause.Comparisons[0]
            : throw new InvalidOperationException($"Runtime test query '{query.QueryIdentity}' must have one comparison.");
        if (comparison.Path != path || comparison.Operator != QueryComparisonOperator.Equal || comparison.Values.Count != 1)
            throw new InvalidOperationException($"Runtime test query '{query.QueryIdentity}' has an unexpected shape.");

#pragma warning disable GW0004
        var matches = await documents.QueryAsync(
            new DocumentStoreQuery(query.DocumentKind, index, comparison.Values[0]!),
            cancellationToken);
#pragma warning restore GW0004
        IEnumerable<DocumentEnvelope> page = matches.Skip(query.Skip ?? 0);
        if (query.Take is { } take)
            page = page.Take(take);
        return new DocumentQueryResult(page.ToArray(), matches.Count);
    }

    public async Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        (await QueryAsync(query, cancellationToken)).TotalCount;

    public async Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        (await QueryAsync(query.Page(query.Skip, 1), cancellationToken)).Documents.FirstOrDefault();

    public async Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        await FirstOrDefaultAsync(query, cancellationToken) is not null;

}
