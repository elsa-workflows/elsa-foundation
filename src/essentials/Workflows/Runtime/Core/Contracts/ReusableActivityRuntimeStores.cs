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
    ValueTask<bool> DeleteAsync(string sourceReferenceId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);
    ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(
        WorkflowExecutableSourceReferenceCleanupBatch batch,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
