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
}

public interface IExecutableActivityTemplateStore : IExecutableActivityTemplateReader, IExecutableActivityTemplateWriter;

public interface IActivityExecutionHierarchyReader
{
    ValueTask<ActivityExecutionHierarchyPage?> ReadPageAsync(ActivityExecutionHierarchyQuery query, CancellationToken cancellationToken = default);
    ValueTask<ActivityExecutionBoundary?> FindBoundaryAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default);
    ValueTask<ActivityExecutionAttemptNavigation?> FindAttemptNavigationAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default);
    ValueTask<ActivityExecutionLayout?> FindLayoutAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default);
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

    /// <summary>
    /// One finite page of the references minted for a definition version, in every scope.
    /// </summary>
    /// <remarks>
    /// Defaulted rather than required so an in-process reader double stays compilable, and defaulted to a
    /// <em>correct but unnarrowed</em> residual filter over <see cref="ListPageAsync"/> rather than to a throw,
    /// so a double that is handed to a real consumer answers instead of faulting. The default is not the
    /// contract's intent: a durable store must override it with a declared by-definition-version route, and
    /// both shipped stores do.
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
    ValueTask<bool> DeleteAsync(string sourceReferenceId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);
    ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(
        WorkflowExecutableSourceReferenceCleanupBatch batch,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
