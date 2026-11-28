using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Dashboard;

public sealed class InMemoryWorkflowPortfolioDataSource(
    IReadOnlyCollection<WorkflowDefinition> definitions,
    IReadOnlyCollection<WorkflowDefinitionDraft> drafts,
    IReadOnlyCollection<WorkflowExecutableSourceReference> sourceReferences) : IWorkflowPortfolioDataSource
{
    public bool IsAvailable => true;

    public ValueTask<WorkflowPortfolioBaseCounts> QueryBaseCountsAsync(
        string tenantId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        var activeIds = ActiveDefinitions(tenantId).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var currentDrafts = CurrentDrafts(tenantId, activeIds).ToArray();
        var publishedIds = sourceReferences
            .Where(x => x.Scope == WorkflowExecutableReferenceScope.Published && x.IsLive(asOf))
            .Select(x => x.DefinitionId)
            .ToHashSet(StringComparer.Ordinal);
        return ValueTask.FromResult(new WorkflowPortfolioBaseCounts(
            activeIds.Count,
            activeIds.Count(publishedIds.Contains),
            currentDrafts.Length));
    }

    public async IAsyncEnumerable<WorkflowDefinitionDraft> StreamCurrentDraftsAsync(
        string tenantId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        var activeIds = ActiveDefinitions(tenantId).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var draft in CurrentDrafts(tenantId, activeIds))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return draft;
        }
    }

    private IEnumerable<WorkflowDefinition> ActiveDefinitions(string tenantId) => definitions
        .Where(x => StringComparer.Ordinal.Equals(x.TenantId, tenantId) && x.DeletedAt is null);

    private IEnumerable<WorkflowDefinitionDraft> CurrentDrafts(string tenantId, IReadOnlySet<string> activeIds) => drafts
        .Where(x => StringComparer.Ordinal.Equals(x.TenantId, tenantId) && activeIds.Contains(x.WorkflowDefinitionId))
        .GroupBy(x => x.WorkflowDefinitionId, StringComparer.Ordinal)
        .Select(group => group.OrderByDescending(x => x.LastModifiedAt)
            .ThenByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id, StringComparer.Ordinal)
            .First());
}

public sealed class UnavailableWorkflowPortfolioDataSource : IWorkflowPortfolioDataSource
{
    public bool IsAvailable => false;
    public ValueTask<WorkflowPortfolioBaseCounts> QueryBaseCountsAsync(string tenantId, DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Unavailable portfolio provider cannot execute queries.");
    public async IAsyncEnumerable<WorkflowDefinitionDraft> StreamCurrentDraftsAsync(
        string tenantId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
