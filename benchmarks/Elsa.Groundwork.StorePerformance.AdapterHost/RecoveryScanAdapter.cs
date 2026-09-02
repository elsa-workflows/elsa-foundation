using System.Security.Cryptography;
using System.Text;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Groundwork v2 recovery-scan adapter. The workload drives the public scanner contract; this leaf only
/// composes the production runtime family over one provider connection and isolated DI scopes.
/// </summary>
internal sealed class RecoveryScanAdapter(
    RunRequest request,
    string connectionString,
    string outputDirectory)
    : IBenchmarkAdapter, IRuntimeRecoveryScanWorkloadAdapter
{
    internal const string PhysicalForm = "recovery-candidate-index";
    private readonly string persistenceScope = PersistenceScopeFor(request);

    string IRuntimeRecoveryScanWorkloadAdapter.PersistenceScope => persistenceScope;

    private RuntimeStoreComposition? composition;
    private WritePathRoundTripObserver? observer;
    private IReadOnlyList<IBenchmarkOperation>? operations;

    public IProviderRoundTripObserver? RoundTripObserver => observer;

    public IReadOnlyList<IBenchmarkOperation> Operations =>
        operations ?? throw new PerformanceContractException(
            "The recovery-scan operations were requested before correctness preparation completed.");

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (composition is not null)
            return;

        observer ??= new WritePathRoundTripObserver(request.Provider);
        composition = await RuntimeStoreComposition.CreateAsync(
            request.Provider,
            connectionString,
            persistenceScope,
            cancellationToken,
            observer);
    }

    public async Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken)
    {
        _ = composition ?? throw new PerformanceContractException(
            "The recovery-scan adapter has no composition; PrepareAsync must run first.");
        var document = NativePlanEvidenceStaging.PublishInto(outputDirectory, request);
        var observed = await ProviderProbe.ReadAsync(request.Provider, connectionString, cancellationToken);
        var workload = new RuntimeRecoveryScanWorkload();
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

    public ValueTask<RuntimeRecoveryScanClient> OpenClientAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(RequireComposition().CreateRecoveryScanClient());
    }

    public ValueTask<RuntimeRecoveryScanClient> ReopenClientAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(RequireComposition().CreateRecoveryScanClient());
    }

    public async ValueTask DisposeAsync()
    {
        if (composition is not null)
            await composition.DisposeAsync();
        composition = null;
        observer = null;
        operations = null;
    }

    private RuntimeStoreComposition RequireComposition() => composition ?? throw new PerformanceContractException(
        "The recovery-scan adapter has no composition; PrepareAsync must run first.");

    internal static string PersistenceScopeFor(RunRequest request)
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
        return $"benchmark-recovery-{digest}";
    }

    private sealed class BenchmarkOperation(IRuntimeRecoveryScanWorkloadOperation operation) : IBenchmarkOperation
    {
        public string Id => operation.Id;
        public Task PrepareInvocationAsync(long invocation, CancellationToken cancellationToken) =>
            operation.PrepareInvocationAsync(invocation, cancellationToken).AsTask();
        public Task InvokeAsync(long invocation, CancellationToken cancellationToken) =>
            operation.InvokeAsync(invocation, cancellationToken).AsTask();
    }
}
