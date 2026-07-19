using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="ISchedulerStateStore"/>. <see cref="SchedulerState"/> exposes two public
/// constructors, which the serializer cannot disambiguate, and the runtime domain core is intentionally
/// free of serialization attributes. The bridge therefore round-trips through a local document shape with
/// a single constructor and rebuilds the domain instance through its canonical constructor. A constant
/// collection partition lets the unfiltered <see cref="ListAsync"/> use the declared-index equality query.
/// </summary>
public sealed class GroundworkSchedulerStateStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.SchedulerStateDocumentKind, boundedStore), ISchedulerStateStore
{
    public async ValueTask<SchedulerState> SaveAsync(SchedulerState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);

        var existing = await LoadByWorkflowExecutionIdAsync(state.WorkflowExecutionId, cancellationToken);
        var document = new SchedulerStateDocument(
            ElsaRuntimeStorageManifest.SchedulerStateDocumentKind,
            SchedulerStatePayload.From(state));
        var result = await SaveDocumentAsync(
            state.WorkflowExecutionId,
            document,
            cancellationToken,
            expectedVersion: existing?.Version ?? 0);
        if (result.Status != DocumentStoreWriteStatus.Saved)
        {
            if (result.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
                await LoadByWorkflowExecutionIdAsync(state.WorkflowExecutionId, cancellationToken);
            throw new InvalidOperationException($"Groundwork rejected scheduler state for workflow execution '{state.WorkflowExecutionId}' with status '{result.Status}'.");
        }

        return state;
    }

    public async ValueTask<SchedulerState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        return (await LoadByWorkflowExecutionIdAsync(workflowExecutionId, cancellationToken))?.State;
    }

    public async ValueTask<IReadOnlyCollection<SchedulerState>> ListAsync(CancellationToken cancellationToken = default) =>
        await QueryDocumentsAsync<SchedulerStateDocument, SchedulerState>(
            ElsaRuntimeStorageManifest.ListAllQuery,
            ElsaRuntimeStorageManifest.CollectionField,
            ElsaRuntimeStorageManifest.SchedulerStateDocumentKind,
            document => document.State.ToDomain(),
            cancellationToken);

    private async ValueTask<LoadedSchedulerState?> LoadByWorkflowExecutionIdAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        var envelope = await Store.LoadAsync(DocumentKind, workflowExecutionId, cancellationToken);
        if (envelope is null)
            return null;

        var document = Serializer.Deserialize<SchedulerStateDocument>(envelope);
        var state = document.State.ToDomain();
        if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, workflowExecutionId))
            throw new InvalidOperationException(
                $"Groundwork physical document identity collision detected for scheduler state in workflow execution '{workflowExecutionId}'.");

        return new LoadedSchedulerState(state, envelope.Version);
    }

    private sealed record SchedulerStateDocument(string Collection, SchedulerStatePayload State);

    private sealed record LoadedSchedulerState(SchedulerState State, long Version);

    // Single-constructor mirror of SchedulerState so the serializer can round-trip it without the domain
    // model carrying provider-specific serialization annotations.
    private sealed record SchedulerStatePayload(
        string WorkflowExecutionId,
        long Version,
        IReadOnlyCollection<ScheduledActivityWorkItem> PendingWork,
        IReadOnlyCollection<SchedulerContinuationWorkItem> PendingContinuations,
        IReadOnlyCollection<VolatileWaitRegistration> VolatileWaits,
        IReadOnlyCollection<SchedulerCompletionWorkItem> PendingCompletionWork,
        IReadOnlyCollection<GeneratorRegistration> ActiveGenerators,
        IReadOnlyCollection<SchedulerGeneratedEventWorkItem> PendingGeneratedEvents)
    {
        public static SchedulerStatePayload From(SchedulerState state) => new(
            state.WorkflowExecutionId,
            state.Version,
            state.PendingWork,
            state.PendingContinuations,
            state.VolatileWaits,
            state.PendingCompletionWork,
            state.ActiveGenerators,
            state.PendingGeneratedEvents);

        public SchedulerState ToDomain() => new(
            WorkflowExecutionId,
            Version,
            PendingWork,
            PendingContinuations,
            VolatileWaits,
            PendingCompletionWork,
            ActiveGenerators,
            PendingGeneratedEvents);
    }
}
