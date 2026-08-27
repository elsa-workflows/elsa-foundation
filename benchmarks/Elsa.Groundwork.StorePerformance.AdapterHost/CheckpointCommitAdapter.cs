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

    public IProviderRoundTripObserver? RoundTripObserver => composition?.Observer;

    /// <summary>
    /// The measured operation sequence is not implemented, and this refuses rather than substituting
    /// something adjacent. The five operations the frozen spec names
    /// (<c>specs/094-harden-groundwork-stores/workloads/runtime.json</c>) are seed-fenced-executions,
    /// commit-checkpoint-bundle, replay-equivalent-commit, attempt-stale-fence-commit and
    /// reopen-and-read-committed-bundle.
    ///
    /// They cannot be derived from <see cref="RuntimeCheckpointCommitWorkload"/>: it exposes only
    /// <c>ExecuteAsync</c>, and everything that builds the bundle — execution ids, activity and
    /// durable-value changes, outbox entries, payload sizing, fencing tokens — is private to it. A leaf that
    /// guessed at that shape would still emit a digest, a duration and a round-trip count, and the artifact
    /// would look well-formed while describing a different bundle than the frozen scenario names. Refusing
    /// is this harness's own rule: a missing adapter is a blocked run, never a simulated result.
    /// </summary>
    public IReadOnlyList<IBenchmarkOperation> Operations =>
        throw new PerformanceContractException(
            "The checkpoint-commit measured operation sequence is not implemented on the v2 adapter. " +
            "Correctness verification is available; measured runs are blocked. See the adapter host README.");

    /// <summary>
    /// The frozen scenario stamps every committed state with this tenant, and the checkpoint writer's
    /// EnsureTenantScope refuses a commit whose ambient scope differs — so this is the only scope the
    /// correctness baseline can run in. v1 imposed the same requirement; the handover README marked it
    /// unverified on v2, and the first live correctness run answered it.
    /// </summary>
    private const string ScenarioPersistenceScope = "tenant-checkpoint";

    public async Task PrepareAsync(CancellationToken cancellationToken) =>
        composition ??= await RuntimeStoreComposition.CreateAsync(
            request.Provider, connectionString, ScenarioPersistenceScope, cancellationToken);

    /// <summary>
    /// Runs the catalog-owned correctness baseline and reports the digest it actually produced.
    ///
    /// The digest is the substantive claim: it is computed from what the public stores really committed and
    /// read back, and <c>ValidateCorrectness</c> compares it against the frozen
    /// <c>workload.Correctness.ResultDigestSha256</c>. A wrong commit path cannot produce it.
    ///
    /// The provider identity fields are echoed from the request, and that is a real gap rather than a
    /// design choice: <c>probe-provider</c> does not yet read sanitized provider configuration back off a
    /// live connection, so the host has nothing independent to report. <c>ValidateCorrectness</c> requires
    /// observed identity to equal requested identity exactly, so echoing is the only thing that can pass
    /// today — which means those three fields currently prove the operator was self-consistent, not that
    /// the provider was what they said. Do not read them as observation until probe-provider is finished.
    /// </summary>
    public async Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken)
    {
        Require();

        // Publishes the operator-captured plan document into the artifact directory and fails if it does
        // not hash to the requested commitment. checkpoint-commit declares no required native routes, so
        // the document's route list is expected to be empty; ValidateCorrectness enforces that count.
        var document = NativePlanEvidenceStaging.PublishInto(outputDirectory, request);

        var result = await new RuntimeCheckpointCommitWorkload().ExecuteAsync(this, cancellationToken);

        return new CorrectnessEvidence(
            result.ResultDigest,
            request.ProviderVersion,
            request.ProviderTopology,
            request.ProviderConfiguration,
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
}
