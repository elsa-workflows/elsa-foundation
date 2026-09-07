using System.Security.Cryptography;
using System.Text;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// The <c>checkpoint-commit</c> adapter leaf on Groundwork v2.
///
/// It drives the catalog-owned workload through the public runtime stores only, over one adapter-owned
/// provider connection, and counts provider round trips with <see cref="WritePathRoundTripObserver"/>.
/// Because <c>GroundworkStorageSessionSource</c> forwards a registered <c>IProviderCommandObserver</c> to
/// every session and unit of work it opens, the commands observed here are the production commit path
/// rather than a hand-rolled sequence of elementary store calls standing in for it — reads included, which
/// the retired write-path observer could not see.
/// </summary>
internal sealed class CheckpointCommitAdapter(
    RunRequest request,
    string connectionString,
    string outputDirectory)
    : IBenchmarkAdapter, IRuntimeCheckpointCommitWorkloadAdapter
{
    private RuntimeStoreComposition? composition;
    private readonly string persistenceScope = PersistenceScopeFor(request);

    public IProviderRoundTripObserver? RoundTripObserver => composition?.Observer;

    /// <summary>
    /// Keeps the four sequential matrix children isolated when they share one configured provider. The
    /// scope is deterministic for one immutable run identity, so a retry in the same child scope is also
    /// an equivalent replay rather than a new conflicting fixture.
    /// </summary>
    public string PersistenceScope => persistenceScope;

    private IReadOnlyList<IBenchmarkOperation>? operations;

    /// <summary>
    /// The workload owns the phase definitions and their representative fixtures. The adapter only adapts
    /// those provider-neutral operations to the process-measurement contract after correctness succeeds.
    /// </summary>
    public IReadOnlyList<IBenchmarkOperation> Operations =>
        operations ?? throw new PerformanceContractException(
            "The checkpoint-commit operations were requested before correctness preparation completed.");

    /// <summary>
    /// The workload stamps every committed state with this adapter-selected scope, and the checkpoint
    /// writer's EnsureTenantScope refuses a commit whose ambient scope differs. The scope is therefore
    /// passed into both the composition and the provider-neutral fixture.
    /// </summary>
    public async Task PrepareAsync(CancellationToken cancellationToken) =>
        composition ??= await RuntimeStoreComposition.CreateAsync(
            request.Provider, connectionString, persistenceScope, cancellationToken);

    /// <summary>
    /// Runs the catalog-owned correctness baseline and reports the digest it actually produced.
    ///
    /// The digest is the substantive claim: it is computed from what the public stores really committed and
    /// read back, and <c>ValidateCorrectness</c> compares it against the frozen
    /// <c>workload.Correctness.ResultDigestSha256</c>. A wrong commit path cannot produce it.
    ///
    /// Provider identity and sanitized driver configuration are read from a live native handshake. The
    /// request is compared to that observation by <c>ArtifactAdmission.ValidateCorrectness</c>; a stale or
    /// hand-edited request therefore blocks rather than becoming provenance.
    /// </summary>
    public async Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken)
    {
        Require();

        // Publishes the operator-captured plan document into the artifact directory and fails if it does
        // not hash to the requested commitment. checkpoint-commit declares no required native routes, so
        // the document's route list is expected to be empty; ValidateCorrectness enforces that count.
        var document = NativePlanEvidenceStaging.PublishInto(outputDirectory, request);
        var observed = await ProviderProbe.ReadAsync(request.Provider, connectionString, cancellationToken);

        var result = await new RuntimeCheckpointCommitWorkload().ExecuteAsync(this, cancellationToken);
        operations = (await new RuntimeCheckpointCommitWorkload().PrepareMeasuredOperationsAsync(this, cancellationToken))
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

    /// <summary>
    /// Two clients from two scopes. The workload's <c>RequireIndependentClients</c> rejects clients that
    /// share a scoped store instance, so these must not come from one scope.
    /// </summary>
    public ValueTask<RuntimeCheckpointCommitClients> OpenIndependentClientsAsync(CancellationToken cancellationToken = default)
    {
        var active = Require();
        return ValueTask.FromResult(new RuntimeCheckpointCommitClients(active.CreateClient(), active.CreateClient()));
    }

    public ValueTask<RuntimeCheckpointCommitClient> ReopenClientAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Require().CreateClient());

    public async ValueTask DisposeAsync()
    {
        if (composition is not null)
            await composition.DisposeAsync();
        composition = null;
    }

    private RuntimeStoreComposition Require() =>
        composition ?? throw new PerformanceContractException(
            "The adapter has no composed backing; PrepareAsync must run first.");

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
        return $"benchmark-checkpoint-{digest}";
    }

    private sealed class BenchmarkOperation(IRuntimeCheckpointCommitWorkloadOperation operation) : IBenchmarkOperation
    {
        public string Id => operation.Id;

        public Task PrepareInvocationAsync(long invocation, CancellationToken cancellationToken) =>
            operation.PrepareInvocationAsync(invocation, cancellationToken).AsTask();

        public Task InvokeAsync(long invocation, CancellationToken cancellationToken) =>
            operation.InvokeAsync(invocation, cancellationToken).AsTask();
    }
}
