using System.Security.Cryptography;
using System.Text;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// The Groundwork v2 due-timer selection adapter. The workload owns the frozen correctness and operation
/// definitions; this leaf composes the production durable-timer contract over one provider connection and
/// retains the provider-native command observer used by process measurement.
/// </summary>
internal sealed class DueTimerSelectionAdapter(
    RunRequest request,
    string connectionString,
    string outputDirectory)
    : IBenchmarkAdapter, IDueTimerSelectionWorkloadAdapter
{
    internal const string PhysicalForm = "dedicated-durable-timer-documents";

    private RuntimeStoreComposition? composition;
    private ProviderProbe.Result? observedProvider;
    private IReadOnlyList<IBenchmarkOperation>? operations;
    private readonly string persistenceScope = PersistenceScopeFor(request);

    public IProviderRoundTripObserver? RoundTripObserver => composition?.Observer;

    public IReadOnlyList<IBenchmarkOperation> Operations =>
        operations ?? throw new PerformanceContractException(
            "The due-timer-selection operations were requested before correctness preparation completed.");

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (composition is not null)
            return;

        // Probe before composing the long-lived runtime connection so correctness provenance records the
        // same live provider handshake used to admit the actual Groundwork stores.
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
            "The due-timer-selection adapter has no provider handshake; PrepareAsync must run first.");
        var workload = new RuntimeDueTimerSelectionWorkload();
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

    public ValueTask<DueTimerSelectionClients> OpenIndependentClientsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = Require();
        return ValueTask.FromResult(new DueTimerSelectionClients(
            active.CreateDurableTimerClient(),
            active.CreateDurableTimerClient()));
    }

    public ValueTask<IDurableTimerStore> ReopenClientAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IDurableTimerStore>(Require().CreateDurableTimerClient());
    }

    public async ValueTask DisposeAsync()
    {
        if (composition is not null)
            await composition.DisposeAsync();
        composition = null;
        observedProvider = null;
        operations = null;
    }

    private RuntimeStoreComposition Require() =>
        composition ?? throw new PerformanceContractException(
            "The due-timer-selection adapter has no composed backing; PrepareAsync must run first.");

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
        return $"benchmark-due-timer-{digest}";
    }

    private sealed class BenchmarkOperation(IDueTimerSelectionWorkloadOperation operation) : IBenchmarkOperation
    {
        public string Id => operation.Id;

        public Task PrepareInvocationAsync(long invocation, CancellationToken cancellationToken) =>
            operation.PrepareInvocationAsync(invocation, cancellationToken).AsTask();

        public Task InvokeAsync(long invocation, CancellationToken cancellationToken) =>
            operation.InvokeAsync(invocation, cancellationToken).AsTask();
    }
}
