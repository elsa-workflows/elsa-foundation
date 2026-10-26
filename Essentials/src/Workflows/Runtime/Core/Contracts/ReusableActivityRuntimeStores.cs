using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IExecutableActivityTemplateReader
{
    ValueTask<ExecutableActivityTemplate?> FindAsync(string templateId, CancellationToken cancellationToken = default);
    ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string templateHash, CancellationToken cancellationToken = default);
    ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(
        RuntimeStorePageRequest request,
        CancellationToken cancellationToken = default);
}

public interface IExecutableActivityTemplateWriter
{
    ValueTask SaveAsync(ExecutableActivityTemplate template, CancellationToken cancellationToken = default);
    ValueTask<bool> DeleteAsync(string templateId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);

    /// <summary>Whether maintenance coordinates the exact selected artifact and reference stores.</summary>
    /// <remarks>A template ID is not a workflow artifact ID. Callers must qualify their exact selected executable
    /// and reference stores before invoking any atomic/maintenance port; the port cannot infer unrelated host DI
    /// replacements from its native tuple alone. Unsupported or mixed selected compositions must not invoke it.</remarks>
    bool CanMaintainReferences(IWorkflowExecutableStore executables, IWorkflowExecutableSourceReferenceStore references) => false;

    /// <summary>Atomically saves/adopts immutable template material and its live source reference.</summary>
    /// <remarks>Returns the actual winning reference. Adoption cannot restore retirement, extend expiry or change
    /// source identity. The provider must refuse a conflicting or no-longer-live reference before writing material.</remarks>
    /// <exception cref="NotSupportedException">The selected provider lacks this atomic material boundary.</exception>
    /// <exception cref="ArgumentException">A source reference ID is invalid or names different template material.</exception>
    /// <exception cref="InvalidDataException">An exact template dependency is missing or has a different hash.</exception>
    /// <exception cref="InvalidOperationException">Material/reference conflicts or no live reference can be established.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates.</exception>
    ValueTask<WorkflowExecutableSourceReference> SaveWithReferenceAsync(ExecutableActivityTemplate template,
        WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template); ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException<WorkflowExecutableSourceReference>(new NotSupportedException("Atomic template/reference publication is unavailable."));
    }

    /// <summary>Deletes an unchanged doomed template reference only while no physical executable/template depends on its material.</summary>
    /// <remarks>The final dependency read and reference snapshot deletion must share the native material boundary.
    /// Physical referencers retain their dependencies even before a runtime root is installed. This operation
    /// does not manufacture a workflow deletion guard or delete a live/restored/replaced reference.</remarks>
    /// <exception cref="NotSupportedException">Atomic template/reference maintenance is unavailable.</exception>
    /// <exception cref="InvalidDataException">Persisted material cannot be trusted; retain it.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates.</exception>
    ValueTask<bool> TryDeleteDoomedReferenceAsync(WorkflowExecutableSourceReference expectedReference,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedReference); cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException<bool>(new NotSupportedException("Atomic template/reference maintenance is unavailable."));
    }

    /// <summary>Deletes unchanged template material only with no physical referencer or remaining source reference.</summary>
    /// <remarks>Rechecks the actual native material/reference state at deletion. Every reference must be removed
    /// through its own guarded maintenance first, so collection cannot strand a source row. Unsupported providers refuse.</remarks>
    /// <exception cref="NotSupportedException">Atomic template/reference maintenance is unavailable.</exception>
    /// <exception cref="InvalidDataException">Persisted material cannot be trusted; retain it.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates.</exception>
    ValueTask<bool> TryDeleteUnreferencedAsync(ExecutableActivityTemplate expectedTemplate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedTemplate); cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException<bool>(new NotSupportedException("Atomic template/reference maintenance is unavailable."));
    }
}

public interface IExecutableActivityTemplateStore : IExecutableActivityTemplateReader, IExecutableActivityTemplateWriter;

public interface IActivityExecutionHierarchyReader
{
    ValueTask<ActivityExecutionHierarchyPage?> ReadPageAsync(ActivityExecutionHierarchyQuery query, CancellationToken cancellationToken = default);
    ValueTask<ActivityExecutionBoundary?> FindBoundaryAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default);
    ValueTask<ActivityExecutionAttemptNavigation?> FindAttemptNavigationAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default);
}

public interface IActivityExecutionHierarchyWriter
{
    ValueTask SaveAsync(ActivityExecutionHierarchyRecord record, CancellationToken cancellationToken = default);
}

public interface IActivityExecutionHierarchyStore : IActivityExecutionHierarchyReader, IActivityExecutionHierarchyWriter;

public interface IActivityExecutionHierarchyCursorCodec
{
    string Encode(ActivityExecutionHierarchyCursorState state);
    ActivityExecutionHierarchyCursorState Decode(string cursor);
}

public interface IWorkflowExecutableSourceReferenceReader
{
    ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default);
    ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(
        WorkflowExecutableSourceReferenceArtifactPageQuery query,
        CancellationToken cancellationToken = default);
    ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(
        WorkflowExecutableSourceReferencePageQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>One finite page of references minted for a definition version, in every scope.</summary>
    /// <remarks>
    /// The default is a correct residual filter for in-process reader doubles. Durable stores override it with
    /// a declared by-definition-version route so export does not scan the complete source-reference table.
    /// </remarks>
    ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByDefinitionVersionPageAsync(
        WorkflowExecutableSourceReferenceDefinitionVersionPageQuery query,
        CancellationToken cancellationToken = default) =>
        WorkflowExecutableSourceReferenceReaderDefaults.ListByDefinitionVersionPageAsync(this, query, cancellationToken);

    ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(
        WorkflowExecutableArtifactCandidateBatch candidates,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

public interface IWorkflowExecutableSourceReferenceWriter
{
    ValueTask SaveAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default);
    ValueTask<bool> RetireAsync(string sourceReferenceId, DateTimeOffset deletedAt, string? reason = null, CancellationToken cancellationToken = default);
    /// <summary>
    /// Atomically retires a live reference only when <paramref name="expectedLiveReference"/> is still the stored
    /// snapshot. Implementations that cannot provide this compare-and-retire guarantee must fail closed.
    /// </summary>
    ValueTask<bool> TryRetireAsync(
        WorkflowExecutableSourceReference expectedLiveReference,
        WorkflowExecutableSourceReference retiredReference,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);
    /// <summary>
    /// Atomically restores a retired reference when <paramref name="expectedRetiredReference"/> is still the stored
    /// snapshot. Implementations that cannot provide this compare-and-restore guarantee must fail closed.
    /// </summary>
    ValueTask<bool> TryRestoreAsync(
        WorkflowExecutableSourceReference expectedRetiredReference,
        WorkflowExecutableSourceReference restoredReference,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);
    /// <summary>
    /// Hard-deletes a reference only when <paramref name="expectedDoomedReference"/> is still the stored snapshot and
    /// the stored row is still retired or expired at <paramref name="now"/>. A restore, recreation or other change
    /// since the snapshot was read makes the delete a no-op, so a collector working from a stale listing cannot remove
    /// a reference that was brought back to life. Every implementation must provide this compare-and-delete
    /// guarantee; there is deliberately no unconditional hard delete on this contract.
    /// </summary>
    ValueTask<bool> TryDeleteDoomedAsync(
        WorkflowExecutableSourceReference expectedDoomedReference,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
    /// <summary>Compare-and-delete under the same artifact deletion guard used for the final retention-root read.</summary>
    /// <remarks>The provider must atomically fence guard expiry/replacement against the physical reference deletion.
    /// Unsupported legacy providers refuse; acquiring a guard followed by the unfenced overload is insufficient.</remarks>
    /// <exception cref="NotSupportedException">Atomic artifact/reference guard coordination is unavailable; retain the reference.</exception>
    /// <exception cref="InvalidDataException">Persisted material or reference authority is corrupt.</exception>
    /// <exception cref="InvalidOperationException">Provider failure is contextualized at the provider's domain persistence boundary, preserving its cause.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates.</exception>
    ValueTask<bool> TryDeleteDoomedAsync(WorkflowExecutableSourceReference expectedDoomedReference,
        DateTimeOffset now, WorkflowExecutableDeletionGuard deletionGuard, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<bool>(new NotSupportedException("Guarded source-reference deletion is not supported."));

    ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(
        WorkflowExecutableSourceReferenceCleanupBatch batch,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
