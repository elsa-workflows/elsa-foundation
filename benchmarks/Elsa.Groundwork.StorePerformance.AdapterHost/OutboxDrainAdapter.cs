using System.Security.Cryptography;
using System.Text;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// The Groundwork v2 post-commit outbox adapter. The workload owns the frozen correctness and operation
/// definitions; this leaf composes the public checkpoint and fenced outbox contracts over one provider
/// connection and records the provider-native commands they issue.
/// </summary>
internal sealed class OutboxDrainAdapter(
    RunRequest request,
    string connectionString,
    string outputDirectory,
    bool captureCommands = false)
    : IBenchmarkAdapter, IRuntimeOutboxDrainWorkloadAdapter
{
    internal const string PhysicalForm = "dedicated-post-commit-outbox-documents";

    private RuntimeStoreComposition? composition;
    private ProviderProbe.Result? observedProvider;
    private IReadOnlyList<IBenchmarkOperation>? operations;
    private readonly string persistenceScope = PersistenceScopeFor(request);

    public IProviderRoundTripObserver? RoundTripObserver => composition?.Observer;

    internal WritePathRoundTripObserver CommandObserver =>
        composition?.Observer ?? throw new PerformanceContractException(
            "The outbox-drain adapter has no command observer; PrepareAsync must run first.");

    public string PersistenceScope => persistenceScope;

    public IReadOnlyList<IBenchmarkOperation> Operations =>
        operations ?? throw new PerformanceContractException(
            "The outbox-drain operations were requested before correctness preparation completed.");

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (composition is not null)
            return;

        // SQLite admits one Groundwork connection per database file. Probe before composing the
        // long-lived runtime connection, then retain that live handshake for correctness provenance.
        var observed = await ProviderProbe.ReadAsync(request.Provider, connectionString, cancellationToken);
        var observer = new WritePathRoundTripObserver(request.Provider, captureCommands);
        var created = await RuntimeStoreComposition.CreateAsync(
            request.Provider, connectionString, persistenceScope, cancellationToken, observer);
        observedProvider = observed;
        composition = created;
    }

    public async Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken)
    {
        Require();
        var document = NativePlanEvidenceStaging.PublishInto(outputDirectory, request);
        var observed = observedProvider ?? throw new PerformanceContractException(
            "The outbox-drain adapter has no provider handshake; PrepareAsync must run first.");
        var workload = new RuntimeOutboxDrainWorkload();
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

    public ValueTask<RuntimeOutboxDrainClients> OpenIndependentClientsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = Require();
        return ValueTask.FromResult(new RuntimeOutboxDrainClients(
            active.CreateOutboxClient(),
            active.CreateOutboxClient()));
    }

    public ValueTask<RuntimeOutboxDrainClient> ReopenClientAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Require().CreateOutboxClient());
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
            "The outbox-drain adapter has no composed backing; PrepareAsync must run first.");

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
        return $"benchmark-outbox-{digest}";
    }

    private sealed class BenchmarkOperation(IRuntimeOutboxDrainWorkloadOperation operation) : IBenchmarkOperation
    {
        public string Id => operation.Id;

        public Task PrepareInvocationAsync(long invocation, CancellationToken cancellationToken) =>
            operation.PrepareInvocationAsync(invocation, cancellationToken).AsTask();

        public Task InvokeAsync(long invocation, CancellationToken cancellationToken) =>
            operation.InvokeAsync(invocation, cancellationToken).AsTask();
    }
}
