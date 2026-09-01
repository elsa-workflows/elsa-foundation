using System.Security.Cryptography;
using System.Text;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// The Groundwork v2 recurring-schedule selection adapter. The workload owns the frozen correctness and
/// bounded operation definitions; this leaf composes the public recurring-trigger schedule contract over
/// one provider-backed persistence scope and retains provider-native command observation.
/// </summary>
internal sealed class RecurringScheduleSelectionAdapter(
    RunRequest request,
    string connectionString,
    string outputDirectory)
    : IBenchmarkAdapter, IRuntimeRecurringScheduleSelectionWorkloadAdapter
{
    internal const string PhysicalForm = "dedicated-recurring-schedule-documents";

    private RuntimeStoreComposition? composition;
    private ProviderProbe.Result? observedProvider;
    private IReadOnlyList<IBenchmarkOperation>? operations;
    private readonly ScheduleIdentityMap scheduleIdentityMap = new();
    private readonly string persistenceScope = PersistenceScopeFor(request);

    public IProviderRoundTripObserver? RoundTripObserver => composition?.Observer;

    public IReadOnlyList<IBenchmarkOperation> Operations =>
        operations ?? throw new PerformanceContractException(
            "The recurring-schedule-selection operations were requested before correctness preparation completed.");

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (composition is not null)
            return;

        // Probe before composing the long-lived runtime connection so the correctness evidence records
        // the provider handshake used to admit the actual Groundwork stores.
        var observed = await ProviderProbe.ReadAsync(request.Provider, connectionString, cancellationToken);
        var created = await RuntimeStoreComposition.CreateAsync(
            request.Provider,
            connectionString,
            persistenceScope,
            cancellationToken);
        observedProvider = observed;
        composition = created;
    }

    public async Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken)
    {
        Require();
        var document = NativePlanEvidenceStaging.PublishInto(outputDirectory, request);
        var observed = observedProvider ?? throw new PerformanceContractException(
            "The recurring-schedule-selection adapter has no provider handshake; PrepareAsync must run first.");
        var workload = new RuntimeRecurringScheduleSelectionWorkload();
        var result = await workload.ExecuteAsync(this, cancellationToken);
        operations = (await workload.PrepareMeasuredOperationsAsync(this, cancellationToken))
            .Select(operation => (IBenchmarkOperation)new BenchmarkOperation(operation))
            .ToArray();

        return new CorrectnessEvidence(
            result.ResultDigest,
            observed.Version,
            observed.Topology,
            observed.Configuration,
            new NativePlanEvidence(
                request.NativePlanIdentity,
                request.NativePlanEvidenceReference,
                request.NativePlanContentSha256,
                document.Routes));
    }

    public ValueTask<RuntimeRecurringScheduleSelectionClients> OpenIndependentClientsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = Require();
        return ValueTask.FromResult(new RuntimeRecurringScheduleSelectionClients(
            new WorkloadScheduleStore(active.CreateRecurringScheduleClient(), scheduleIdentityMap),
            new WorkloadScheduleStore(active.CreateRecurringScheduleClient(), scheduleIdentityMap)));
    }

    public ValueTask<IRecurringTriggerScheduleStore> ReopenClientAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IRecurringTriggerScheduleStore>(
            new WorkloadScheduleStore(Require().CreateRecurringScheduleClient(), scheduleIdentityMap));
    }

    public async ValueTask DisposeAsync()
    {
        if (composition is not null)
            await composition.DisposeAsync();
        composition = null;
        observedProvider = null;
        operations = null;
        scheduleIdentityMap.Clear();
    }

    private RuntimeStoreComposition Require() =>
        composition ?? throw new PerformanceContractException(
            "The recurring-schedule-selection adapter has no composed backing; PrepareAsync must run first.");

    private static string PersistenceScopeFor(RunRequest request)
    {
        var identity = string.Join(
            '|',
            request.ComparisonCohortId,
            request.MeasurementSetId,
            request.WorkloadId,
            request.WorkloadVersion,
            request.Provider,
            request.ProviderVersion,
            request.ProviderTopology,
            string.Join(';', request.ProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}")),
            request.Adapter,
            request.PhysicalForm,
            request.Scale,
            request.CommitSha,
            request.HarnessAssemblySha256,
            request.CompositionFingerprint,
            request.HostFingerprintSha256,
            request.Seed,
            request.InputFingerprintSha256,
            request.NativePlanIdentity,
            request.NativePlanEvidenceReference,
            request.NativePlanContentSha256,
            request.ProcessKind,
            request.ProcessIndex);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return $"benchmark-recurring-{digest}";
    }

    private sealed class BenchmarkOperation(IRuntimeRecurringScheduleSelectionWorkloadOperation operation) : IBenchmarkOperation
    {
        public string Id => operation.Id;

        public Task PrepareInvocationAsync(long invocation, CancellationToken cancellationToken) =>
            operation.PrepareInvocationAsync(invocation, cancellationToken).AsTask();

        public Task InvokeAsync(long invocation, CancellationToken cancellationToken) =>
            operation.InvokeAsync(invocation, cancellationToken).AsTask();
    }

    /// <summary>
    /// Adapts the catalog's stable workload ids to the deterministic ids required by the public runtime
    /// schedule contract. The workload ids are part of the frozen result vector, while Groundwork validates
    /// persisted ids from (publication, artifact, executable node); this map keeps that translation entirely
    /// at the public-store boundary and leaves provider calls in the production store implementation.
    /// </summary>
    private sealed class WorkloadScheduleStore(
        IRecurringTriggerScheduleStore inner,
        ScheduleIdentityMap identities) : IRecurringTriggerScheduleStore
    {
        public async ValueTask<RecurringTriggerSchedule> SaveAsync(
            RecurringTriggerSchedule schedule,
            CancellationToken cancellationToken = default)
        {
            var stored = identities.ToStorage(schedule);
            var saved = await inner.SaveAsync(stored, cancellationToken);
            return identities.ToWorkload(saved);
        }

        public async ValueTask PreparePublicationAsync(
            string publicationId,
            IReadOnlyCollection<RecurringTriggerSchedule> schedules,
            CancellationToken cancellationToken = default)
        {
            var stored = schedules.Select(identities.ToStorage).ToArray();
            await inner.PreparePublicationAsync(publicationId, stored, cancellationToken);
        }

        public async ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByPublicationPageAsync(
            RecurringTriggerSchedulePublicationPageQuery query,
            CancellationToken cancellationToken = default)
        {
            var page = await inner.ListByPublicationPageAsync(query, cancellationToken);
            return identities.ToWorkload(page);
        }

        public async ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByArtifactPageAsync(
            RecurringTriggerScheduleArtifactPageQuery query,
            CancellationToken cancellationToken = default)
        {
            var page = await inner.ListByArtifactPageAsync(query, cancellationToken);
            return identities.ToWorkload(page);
        }

        public ValueTask ActivatePublicationAsync(
            string publicationId,
            string? replacedPublicationId,
            CancellationToken cancellationToken = default) =>
            inner.ActivatePublicationAsync(publicationId, replacedPublicationId, cancellationToken);

        public ValueTask DeleteByPublicationAsync(
            string publicationId,
            CancellationToken cancellationToken = default) =>
            inner.DeleteByPublicationAsync(publicationId, cancellationToken);

        public async ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListDueAsync(
            DateTimeOffset asOf,
            int limit,
            CancellationToken cancellationToken = default)
        {
            var schedules = await inner.ListDueAsync(asOf, limit, cancellationToken);
            return schedules.Select(identities.ToWorkload).ToArray();
        }

        public async ValueTask<RecurringTriggerSchedule?> FindAsync(
            string scheduleId,
            CancellationToken cancellationToken = default)
        {
            var schedule = await inner.FindAsync(identities.ToStorageId(scheduleId), cancellationToken);
            return schedule is null ? null : identities.ToWorkload(schedule);
        }

        public ValueTask<bool> TryAdvanceAsync(
            string scheduleId,
            DateTimeOffset expectedNextOccurrence,
            DateTimeOffset newNextOccurrence,
            CancellationToken cancellationToken = default) =>
            inner.TryAdvanceAsync(
                identities.ToStorageId(scheduleId),
                expectedNextOccurrence,
                newNextOccurrence,
                cancellationToken);

        public ValueTask DeleteByArtifactAsync(
            string artifactId,
            CancellationToken cancellationToken = default) =>
            inner.DeleteByArtifactAsync(artifactId, cancellationToken);

        public ValueTask DeleteAsync(string scheduleId, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(identities.ToStorageId(scheduleId), cancellationToken);
    }

    private sealed class ScheduleIdentityMap
    {
        private readonly Dictionary<string, string> workloadToStorage = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> storageToWorkload = new(StringComparer.Ordinal);

        public RecurringTriggerSchedule ToStorage(RecurringTriggerSchedule schedule)
        {
            var storageId = schedule.PublicationId is null
                ? RecurringTriggerSchedule.BuildId(schedule.ArtifactId, schedule.ExecutableNodeId)
                : RecurringTriggerSchedule.BuildId(
                    schedule.PublicationId,
                    schedule.ArtifactId,
                    schedule.ExecutableNodeId);
            lock (this)
            {
                if (workloadToStorage.TryGetValue(schedule.ScheduleId, out var existingStorageId) &&
                    !StringComparer.Ordinal.Equals(existingStorageId, storageId))
                {
                    throw new InvalidOperationException(
                        $"The recurring-schedule workload reused id '{schedule.ScheduleId}' for a different public identity.");
                }

                if (storageToWorkload.TryGetValue(storageId, out var existingWorkloadId) &&
                    !StringComparer.Ordinal.Equals(existingWorkloadId, schedule.ScheduleId))
                {
                    throw new InvalidOperationException(
                        $"The recurring-schedule workload mapped distinct ids to public identity '{storageId}'.");
                }

                workloadToStorage[schedule.ScheduleId] = storageId;
                storageToWorkload[storageId] = schedule.ScheduleId;
            }

            return schedule with { ScheduleId = storageId };
        }

        public string ToStorageId(string workloadId)
        {
            lock (this)
                return workloadToStorage.TryGetValue(workloadId, out var storageId) ? storageId : workloadId;
        }

        public RecurringTriggerSchedule ToWorkload(RecurringTriggerSchedule schedule)
        {
            lock (this)
            {
                return storageToWorkload.TryGetValue(schedule.ScheduleId, out var workloadId)
                    ? schedule with { ScheduleId = workloadId }
                    : schedule;
            }
        }

        public RuntimeStorePage<RecurringTriggerSchedule> ToWorkload(
            RuntimeStorePage<RecurringTriggerSchedule> page) =>
            new(
                new RuntimeStorePageRequest(
                    page.Items.Count == 0 ? RuntimeStorePageRequest.DefaultLimit : page.Items.Count),
                page.Items.Select(ToWorkload).ToArray(),
                page.NextContinuationToken);

        public void Clear()
        {
            lock (this)
            {
                workloadToStorage.Clear();
                storageToWorkload.Clear();
            }
        }
    }
}
