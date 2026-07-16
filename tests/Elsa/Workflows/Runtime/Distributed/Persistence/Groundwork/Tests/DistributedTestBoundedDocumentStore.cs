using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests;

/// <summary>
/// Adapts the legacy standalone provider factory used by this leaf's tests to the route-bound query contract.
/// Production hosts resolve <see cref="IBoundedDocumentStore"/> from their admitted physical runtime instead.
/// </summary>
internal sealed class DistributedTestBoundedDocumentStore(IDocumentStore documents) : IBoundedDocumentStore
{
    public async Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
    {
        var (index, path) = query.QueryIdentity switch
        {
            DistributedGroundworkStorageManifest.ListAllQuery =>
                (DistributedGroundworkStorageManifest.ByCollectionIndex, DistributedGroundworkStorageManifest.CollectionField),
            DistributedGroundworkStorageManifest.ListByWorkflowExecutionQuery =>
                (DistributedGroundworkStorageManifest.ByWorkflowExecutionIndex, DistributedGroundworkStorageManifest.WorkflowExecutionIdField),
            _ => throw new InvalidOperationException($"Undeclared distributed-runtime test query '{query.QueryIdentity}'.")
        };
        var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
        Assert.Equal(path, comparison.Path);
        Assert.Equal(QueryComparisonOperator.Equal, comparison.Operator);

#pragma warning disable GW0004
        var result = await documents.QueryAsync(
            new DocumentStoreQuery(
                query.DocumentKind,
                index,
                Assert.Single(comparison.Values)!,
                query.Skip,
                query.Take),
            cancellationToken);
#pragma warning restore GW0004
        return new DocumentQueryResult(result, result.Count);
    }

    public async Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        (await QueryAsync(query, cancellationToken)).TotalCount;

    public async Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        (await QueryAsync(query.Page(query.Skip, 1), cancellationToken)).Documents.FirstOrDefault();

    public async Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        await FirstOrDefaultAsync(query, cancellationToken) is not null;
}
