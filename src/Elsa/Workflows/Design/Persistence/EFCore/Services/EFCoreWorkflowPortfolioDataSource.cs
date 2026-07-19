using Elsa.Serialization.Core;
using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Dashboard.Providers.EFCore;

public sealed class EFCoreWorkflowPortfolioDataSource(
    IDbContextFactory<WorkflowsDesignDbContext> dbContextFactory,
    IWorkflowExecutableSourceReferenceStore sourceReferences,
    IPayloadSerializer payloadSerializer) : IWorkflowPortfolioDataSource
{
    // This adapter is the official default combination: relational design state plus the default volatile
    // runtime source-reference store. A durable runtime provider must contribute its own joined aggregate.
    public bool IsAvailable => sourceReferences is Elsa.Workflows.Runtime.Core.Services.InMemoryWorkflowExecutableSourceReferenceStore;

    public async ValueTask<WorkflowPortfolioBaseCounts> QueryBaseCountsAsync(
        string tenantId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("EF Core portfolio aggregation requires the default in-memory runtime reference provider.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var active = db.WorkflowDefinitions.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.DeletedAt == null);
        var activeCount = await active.CountAsync(cancellationToken);
        var currentDraftCount = await db.WorkflowDefinitionDrafts.AsNoTracking()
            .Where(x => x.TenantId == tenantId && active.Select(d => d.Id).Contains(x.WorkflowDefinitionId))
            .Select(x => x.WorkflowDefinitionId)
            .Distinct()
            .CountAsync(cancellationToken);
        var publishedIds = (await sourceReferences.ListAsync(
                WorkflowExecutableReferenceScope.Published,
                liveOnly: true,
                now: asOf,
                cancellationToken))
            .Select(x => x.DefinitionId)
            .ToHashSet(StringComparer.Ordinal);
        var publishedCount = 0;
        await foreach (var id in active.Select(x => x.Id).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (publishedIds.Contains(id))
                publishedCount++;
        }

        return new(activeCount, publishedCount, currentDraftCount);
    }

    public async IAsyncEnumerable<WorkflowDefinitionDraft> StreamCurrentDraftsAsync(
        string tenantId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var activeIds = db.WorkflowDefinitions.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.DeletedAt == null)
            .Select(x => x.Id);
        var current = db.WorkflowDefinitionDrafts.AsNoTracking()
            .Where(x => x.TenantId == tenantId && activeIds.Contains(x.WorkflowDefinitionId))
            .Where(draft => !db.WorkflowDefinitionDrafts.Any(other =>
                other.TenantId == tenantId &&
                other.WorkflowDefinitionId == draft.WorkflowDefinitionId &&
                (other.LastModifiedAt > draft.LastModifiedAt ||
                 (other.LastModifiedAt == draft.LastModifiedAt && other.CreatedAt > draft.CreatedAt) ||
                 (other.LastModifiedAt == draft.LastModifiedAt && other.CreatedAt == draft.CreatedAt && string.Compare(other.Id, draft.Id) > 0))));

        await foreach (var draft in current.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            draft.State = string.IsNullOrWhiteSpace(draft.StateSource)
                ? WorkflowDefinitionState.Empty
                : payloadSerializer.Deserialize<WorkflowDefinitionState>(draft.StateSource);
            yield return draft;
        }
    }
}

public static class EFCoreWorkflowPortfolioRegistration
{
    public static IServiceCollection AddEFCoreWorkflowPortfolio(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Scoped<IWorkflowPortfolioDataSource, EFCoreWorkflowPortfolioDataSource>());
        return services;
    }
}
