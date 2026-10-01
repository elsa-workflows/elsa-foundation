using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// A source-reference store whose retires by id fail, as one that becomes unavailable just after completion has switched
/// a slot's activation on would (#2251). Every other operation passes through.
/// </summary>
/// <remarks>Shared by the Runtime contract and the Publishing tests, which link this file.</remarks>
internal sealed class ReferencesThatCannotRetire(IWorkflowExecutableSourceReferenceStore inner) : IWorkflowExecutableSourceReferenceStore
{
    public ValueTask<bool> RetireAsync(string sourceReferenceId, DateTimeOffset deletedAt, string? reason = null, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<bool>(new InvalidOperationException("The source-reference store is unavailable."));

    public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default) => inner.FindAsync(sourceReferenceId, cancellationToken);
    public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(WorkflowExecutableSourceReferencePageQuery query, CancellationToken cancellationToken = default) => inner.ListPageAsync(query, cancellationToken);
    public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(WorkflowExecutableSourceReferenceArtifactPageQuery query, CancellationToken cancellationToken = default) => inner.ListByArtifactPageAsync(query, cancellationToken);
    public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByDefinitionVersionPageAsync(WorkflowExecutableSourceReferenceDefinitionVersionPageQuery query, CancellationToken cancellationToken = default) => inner.ListByDefinitionVersionPageAsync(query, cancellationToken);
    public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(WorkflowExecutableArtifactCandidateBatch candidates, DateTimeOffset now, CancellationToken cancellationToken = default) => inner.ListUnreferencedArtifactIdsAsync(candidates, now, cancellationToken);
    public ValueTask SaveAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default) => inner.SaveAsync(reference, cancellationToken);
    public ValueTask<bool> TryRetireAsync(WorkflowExecutableSourceReference expectedLiveReference, WorkflowExecutableSourceReference retiredReference, CancellationToken cancellationToken = default) => inner.TryRetireAsync(expectedLiveReference, retiredReference, cancellationToken);
    public ValueTask<bool> TryRestoreAsync(WorkflowExecutableSourceReference expectedRetiredReference, WorkflowExecutableSourceReference restoredReference, CancellationToken cancellationToken = default) => inner.TryRestoreAsync(expectedRetiredReference, restoredReference, cancellationToken);
    public ValueTask<bool> TryDeleteDoomedAsync(WorkflowExecutableSourceReference expectedDoomedReference, DateTimeOffset now, CancellationToken cancellationToken = default) => inner.TryDeleteDoomedAsync(expectedDoomedReference, now, cancellationToken);
    public ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(WorkflowExecutableSourceReferenceCleanupBatch batch, DateTimeOffset now, CancellationToken cancellationToken = default) => inner.DeleteExpiredOrRetiredAsync(batch, now, cancellationToken);
}
