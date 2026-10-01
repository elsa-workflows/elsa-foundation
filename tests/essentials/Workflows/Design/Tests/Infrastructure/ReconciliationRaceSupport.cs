using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Reconciliation;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Tests.Infrastructure;

// Shared by the SQLite convergence tests and the native provider tests, which link this file.

/// <summary>Contributes the given versions to a reconciliation pass, as the aggregating handler does for real sources.</summary>
internal sealed class ContributingPublisher(params IWorkflowDefinitionVersion[] versions) : IInlineEventPublisher
{
    public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event is WorkflowVersionsReconciling reconciling)
            foreach (var version in versions)
                reconciling.Versions.Add(version);
        return Task.CompletedTask;
    }
}

/// <summary>A point at which a pass stops until the test lets it go on.</summary>
internal sealed class Pause
{
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the pass stops here, or fails with the pass if it ends first.</summary>
    public async Task ReachedBy(Task pass) => await await Task.WhenAny(_reached.Task, pass);

    public void Resume() => _resumed.SetResult();

    public Task HoldAsync()
    {
        _reached.SetResult();
        return _resumed.Task;
    }
}

/// <summary>
/// Stops a pass right after its first definition read, so another writer can commit before the pass writes. Optionally
/// stops it again before its second read, the re-read after a lost race.
/// </summary>
internal sealed class PausingDefinitionStore(IWorkflowDefinitionStore inner, Pause afterFirstRead, Pause? beforeSecondRead = null) : IWorkflowDefinitionStore
{
    private int _reads;

    public async Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var read = Interlocked.Increment(ref _reads);
        if (read == 2 && beforeSecondRead is not null)
            await beforeSecondRead.HoldAsync();

        var definition = await inner.FindByIdAsync(id, cancellationToken);
        if (read == 1)
            await afterFirstRead.HoldAsync();
        return definition;
    }

    public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default) =>
        inner.ListAsync(filter, cancellationToken);
}
