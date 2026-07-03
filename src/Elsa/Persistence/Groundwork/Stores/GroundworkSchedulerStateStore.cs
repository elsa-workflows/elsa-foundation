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
public sealed class GroundworkSchedulerStateStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer) : ISchedulerStateStore
{
    public async ValueTask<SchedulerState> SaveAsync(SchedulerState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);

        var document = new SchedulerStateDocument(
            ElsaRuntimeStorageManifest.SchedulerStateDocumentKind,
            SchedulerStatePayload.From(state));
        var (schemaVersion, content) = serializer.Serialize(ElsaRuntimeStorageManifest.SchedulerStateDocumentKind, document);

        await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.SchedulerStateDocumentKind,
                state.WorkflowExecutionId,
                schemaVersion,
                content),
            cancellationToken);

        return state;
    }

    public async ValueTask<SchedulerState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.SchedulerStateDocumentKind,
            workflowExecutionId,
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<IReadOnlyCollection<SchedulerState>> ListAsync(CancellationToken cancellationToken = default)
    {
        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.SchedulerStateDocumentKind,
                ElsaRuntimeStorageManifest.ByCollectionIndex,
                ElsaRuntimeStorageManifest.SchedulerStateDocumentKind),
            cancellationToken);

        return envelopes.Select(Map).ToArray();
    }

    private SchedulerState Map(DocumentEnvelope envelope) =>
        serializer.Deserialize<SchedulerStateDocument>(envelope).State.ToDomain();

    private sealed record SchedulerStateDocument(string Collection, SchedulerStatePayload State);

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
