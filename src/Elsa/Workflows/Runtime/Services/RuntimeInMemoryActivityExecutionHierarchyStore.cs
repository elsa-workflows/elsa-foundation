using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class RuntimeInMemoryActivityExecutionHierarchyStore(IActivityExecutionHierarchyCursorCodec cursorCodec) : IActivityExecutionHierarchyStore
{
    private readonly Dictionary<(string WorkflowExecutionId, string ActivityExecutionId), ActivityExecutionHierarchyRecord> _records = new();
    private readonly Dictionary<(string WorkflowExecutionId, string ActivityExecutionId), ActivityExecutionLayout> _layouts = new();
    private readonly Lock _gate = new();

    public ValueTask SaveAsync(ActivityExecutionHierarchyRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(record);
        lock (_gate)
            _records[(record.WorkflowExecutionId, record.ActivityExecutionId)] = record;
        return ValueTask.CompletedTask;
    }

    public void SaveLayout(ActivityExecutionLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        lock (_gate)
            _layouts[(layout.WorkflowExecutionId, layout.ActivityExecutionId)] = layout;
    }

    public ValueTask<ActivityExecutionHierarchyPage?> ReadPageAsync(ActivityExecutionHierarchyQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(query);
        lock (_gate)
            return ValueTask.FromResult(ActivityExecutionHierarchyPager.Read(query, _records.Values.ToArray(), cursorCodec));
    }

    public ValueTask<ActivityExecutionBoundary?> FindBoundaryAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return ValueTask.FromResult(ActivityExecutionHierarchyProjector.FindBoundary(
                _records.Values.Where(x => StringComparer.Ordinal.Equals(x.WorkflowExecutionId, workflowExecutionId)).ToArray(),
                activityExecutionId));
    }

    public ValueTask<ActivityExecutionAttemptNavigation?> FindAttemptNavigationAsync(
        string workflowExecutionId,
        string activityExecutionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return ValueTask.FromResult(ActivityExecutionHierarchyProjector.FindAttemptNavigation(
                _records.Values.Where(x => StringComparer.Ordinal.Equals(x.WorkflowExecutionId, workflowExecutionId)).ToArray(),
                activityExecutionId));
    }

    public ValueTask<ActivityExecutionLayout?> FindLayoutAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return ValueTask.FromResult(_layouts.GetValueOrDefault((workflowExecutionId, activityExecutionId)));
    }

    private static void Validate(ActivityExecutionHierarchyRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!StringComparer.Ordinal.Equals(record.WorkflowExecutionId, record.Item.WorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(record.ActivityExecutionId, record.Item.ActivityExecutionId) ||
            record.ExecutionSequence != record.Item.ExecutionSequence)
            throw new ArgumentException("Hierarchy record envelope fields must match the item.", nameof(record));
    }
}
