using System.Collections.Concurrent;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Reconciliation;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Reconciliation.Contracts;
using Elsa.Workflows.Design.Reconciliation.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Tests.Infrastructure;

// Shared by the SQLite convergence tests, the native provider tests and the publishing cold-start race, which link this file.

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

/// <summary>Delivers each <typeparamref name="TEvent"/> to its real aggregating handler and drops every other event.</summary>
internal sealed class HandlingPublisher<TEvent>(IEventHandler<TEvent> handler) : IInlineEventPublisher where TEvent : IEvent
{
    public Task Publish(IEvent @event, CancellationToken cancellationToken = default) =>
        @event is TEvent handled ? handler.Handle(handled, cancellationToken) : Task.CompletedTask;
}

/// <summary>
/// The credential-literal rule (spec 188) of the passes these races run. Their states hold no activity, so the rule
/// has nothing to judge and this validator finds nothing, as the real one would; it refuses to judge a state that does
/// hold one, which needs the real validator. The rule itself is tested where it is judged.
/// </summary>
internal sealed class NothingToJudgeValidator : ICredentialLiteralValidator
{
    public static NothingToJudgeValidator Instance { get; } = new();

    public ValueTask<IReadOnlyList<ValidationError>> Validate(WorkflowDefinitionState state, CancellationToken cancellationToken) =>
        state.RootActivity is null
            ? ValueTask.FromResult<IReadOnlyList<ValidationError>>([])
            : throw new InvalidOperationException("These races reconcile states without activities; a state with one needs the real validator.");
}

/// <summary>A reconciliation source that lists the same entries on every read.</summary>
internal sealed class StaticWorkflowSource(params WorkflowVersionReconciliationModel[] entries) : IWorkflowReconciliationSource
{
    public string SourceId => "static";

    public string SourceKind => "test";

    public ValueTask<IEnumerable<WorkflowVersionReconciliationModel>> Read(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IEnumerable<WorkflowVersionReconciliationModel>>(entries);
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

    /// <summary>
    /// Waits until every pass has stopped at its own pause, then lets them all go on, so each one writes on what it read
    /// before any of the others wrote.
    /// </summary>
    public static async Task ReleaseTogether(params (Task Pass, Pause Pause)[] holds)
    {
        foreach (var (pass, pause) in holds)
            await pause.ReachedBy(pass);
        foreach (var (_, pause) in holds)
            pause.Resume();
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

/// <summary>Stops a pass right after it first finds out whether its version exists, before it materializes the version.</summary>
internal sealed class PausingVersionStore(IWorkflowDefinitionVersionStore inner, Pause afterFirstExistsCheck) : IWorkflowDefinitionVersionStore
{
    private int _checks;

    public async Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default)
    {
        var exists = await inner.ExistsAsync(definitionId, semVerSortKey, cancellationToken);
        if (Interlocked.Increment(ref _checks) == 1)
            await afterFirstExistsCheck.HoldAsync();
        return exists;
    }

    public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => inner.GetAsync(versionId, cancellationToken);

    public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) =>
        inner.FindByIdAsync(versionId, cancellationToken);

    public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
        inner.GetWithDefinitionAsync(versionId, cancellationToken);

    public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) =>
        inner.FindLatestVersionAsync(definitionId, cancellationToken);

    public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
        inner.ListByDefinitionAsync(definitionId, cancellationToken);
}

/// <summary>Records how each design write ended, so a test can tell a write that committed from one that replayed another's.</summary>
internal sealed class RecordingAtomicWriter(IDesignAtomicWriter inner, ConcurrentQueue<(string OperationKind, DesignAtomicWriteStatus Status)> outcomes)
    : IDesignAtomicWriter
{
    public async Task<DesignAtomicWriteResult<T>> ExecuteAsync<T>(
        DesignOperationKey operationKey,
        string operationKind,
        object requestMaterial,
        IReadOnlyCollection<string> mutatedUnits,
        Func<IDesignAtomicWriteContext, CancellationToken, Task<DesignAtomicWriteStage<T>>> stage,
        Func<CancellationToken, Task>? beforeAttempt = null,
        CancellationToken cancellationToken = default,
        IDesignAtomicWriteResultCodec<T>? resultCodec = null)
    {
        var result = await inner.ExecuteAsync(operationKey, operationKind, requestMaterial, mutatedUnits, stage, beforeAttempt, cancellationToken, resultCodec);
        outcomes.Enqueue((operationKind, result.Status));
        return result;
    }
}
