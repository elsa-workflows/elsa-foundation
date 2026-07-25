using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

/// <summary>
/// Opt-in publication for the narrow runtime slice whose ledger obligations are both exercised through
/// a public store and validated by the provider-independent scenario catalog.
/// </summary>
public sealed class RuntimeProviderEvidencePublicationTests
{
    private const string PublishOptIn = "ELSA_PUBLISH_GROUNDWORK_RUNTIME_EVIDENCE";
    private const string EvidenceOutput = "ELSA_GROUNDWORK_EVIDENCE_OUTPUT";
    private const string EvidenceVersion = "0.0.1-preview.85";
    private const string SourceCommit = "ELSA_GROUNDWORK_SOURCE_COMMIT";
    private const string SourceTree = "ELSA_GROUNDWORK_SOURCE_TREE";
    private const string RunIdentity = "ELSA_GROUNDWORK_RUN_IDENTITY";
    private const string SchedulerScenario = "runtime-scheduler-state-roundtrip";

    [Fact]
    public async Task Checkpoint_fence_capture_retains_the_same_execution_observations_and_physical_target()
    {
        var capture = await RuntimeProviderEvidenceScenarios.CaptureCheckpointFenceEvidenceAsync("sqlite");

        Assert.Equal("sqlite", capture.Descriptor.ProviderKey);
        Assert.True(capture.OwnershipFencing.StaleFencingToken < capture.OwnershipFencing.CurrentFencingToken);
        Assert.Equal(1, capture.OwnershipFencing.StaleCommitCount);
        Assert.Equal(1, capture.OwnershipFencing.WinnerCount);
        Assert.Equal(nameof(RuntimeCheckpointReplayConflictException), capture.Idempotency.ConflictExceptionType);
        Assert.Equal(capture.Idempotency.MarkerVersionBeforeReplay, capture.Idempotency.MarkerVersionAfterReplay);
        Assert.True(capture.Idempotency.PendingWorkMatches);
        Assert.Equal(3, capture.FailureRecoveries.Count);
        Assert.All(capture.FailureRecoveries, recovery =>
        {
            Assert.Equal(1, recovery.RecoveredCompleteBundleCount);
            Assert.Equal(0, recovery.RecoveredPartialBundleCount);
        });
        Assert.True(capture.ProcessRestart.UsedDistinctProcess);
        Assert.Equal(1, capture.ProcessRestart.CompleteBundleCount);

        var fencing = RuntimeProviderEvidenceScenarios.CreateCheckpointFenceResult(
            capture,
            "runtime-checkpoint-commit");
        var idempotency = RuntimeProviderEvidenceScenarios.CreateCheckpointIdempotencyResult(
            capture,
            "runtime-checkpoint-commit");
        var restart = RuntimeProviderEvidenceScenarios.CreateCheckpointProcessRestartResult(
            capture,
            "runtime-checkpoint-commit");

        Assert.Equal(capture.PhysicalTargetFingerprint, fencing.CompositionFingerprint);
        Assert.Equal(capture.OwnershipFencing.TokenOrder, Observation(fencing, "token-order"));
        Assert.Equal(
            capture.Idempotency.ConflictExceptionType,
            Observation(idempotency, "conflicting-replay"));
        Assert.Contains(
            capture.ProcessRestart.DocumentVersion.ToString(),
            Observation(restart, "child-process-rehydration"),
            StringComparison.Ordinal);
    }

    [SkippableFact]
    [Trait("Category", "GroundworkEvidencePublication")]
    public async Task Publish_the_catalog_validated_runtime_provider_evidence_slice()
    {
        RequirePublicationOptIn();
        var output = Environment.GetEnvironmentVariable(EvidenceOutput)!;
        var schedulerResults = new List<GroundworkScenarioResult>();

        foreach (var providerKey in GroundworkStoreScenarioCatalog.MandatoryProviderKeys)
            schedulerResults.Add(await RuntimeProviderEvidenceScenarios.RunSchedulerStateReopenAsync(providerKey));

        GroundworkStoreScenarioCatalog.Get(SchedulerScenario)
            .RequireEquivalentProviderResults("runtime-scheduler-state", schedulerResults);

        var publications = new[]
        {
            await GroundworkProviderEvidencePublisher.PublishAsync(
                output,
                GroundworkLedgerObligation.OrdinaryRoundTrip(
                    "runtime-scheduler-state",
                    SchedulerScenario),
                schedulerResults),
            await GroundworkProviderEvidencePublisher.PublishAsync(
                output,
                GroundworkLedgerObligation.RestartScenario(
                    "runtime-scheduler-state",
                    "dispose-and-reopen-same-database",
                    SchedulerScenario),
                schedulerResults)
        };

        Assert.All(publications, publication => Assert.Equal(4, publication.LedgerRecords.Count));
        await GroundworkProviderEvidencePublisher.WriteLedgerAttachmentAsync(output, "runtime", publications);
    }

    [SkippableFact]
    [Trait("Category", "GroundworkEvidencePublication")]
    public async Task Publish_the_checkpoint_and_fence_provider_evidence_slice()
    {
        RequirePublicationOptIn();
        var output = Environment.GetEnvironmentVariable(EvidenceOutput)!;
        var provenance = RequireVersionedPublicationProvenance();
        var captures = new List<RuntimeCheckpointFenceExecutionResult>();

        foreach (var providerKey in GroundworkStoreScenarioCatalog.MandatoryProviderKeys)
            captures.Add(await RuntimeProviderEvidenceScenarios.CaptureCheckpointFenceEvidenceAsync(providerKey));

        var publications = new List<GroundworkProviderEvidencePublication>
        {
            await PublishAsync(
                output,
                provenance,
                GroundworkLedgerObligation.ConcurrencySemantic(
                    "runtime-checkpoint-commit",
                    "atomic-stale-fence-rejection",
                    "runtime-execution-ownership-fencing"),
                captures,
                evidence => RuntimeProviderEvidenceScenarios.CreateCheckpointFenceResult(
                    evidence,
                    "runtime-checkpoint-commit")),
            await PublishAsync(
                output,
                provenance,
                GroundworkLedgerObligation.ConcurrencySemantic(
                    "runtime-execution-liveness",
                    "strictly-increasing-fence",
                    "runtime-execution-ownership-fencing"),
                captures,
                evidence => RuntimeProviderEvidenceScenarios.CreateCheckpointFenceResult(
                    evidence,
                    "runtime-execution-liveness")),
            await PublishAsync(
                output,
                provenance,
                GroundworkLedgerObligation.ConcurrencySemantic(
                    "runtime-checkpoint-commit",
                    "create-only-idempotency-marker",
                    "runtime-checkpoint-idempotency"),
                captures,
                evidence => RuntimeProviderEvidenceScenarios.CreateCheckpointIdempotencyResult(
                    evidence,
                    "runtime-checkpoint-commit")),
            await PublishAsync(
                output,
                provenance,
                GroundworkLedgerObligation.ConcurrencySemantic(
                    "runtime-post-commit-outbox",
                    "checkpoint-bundle-write",
                    "runtime-checkpoint-idempotency"),
                captures,
                evidence => RuntimeProviderEvidenceScenarios.CreateCheckpointIdempotencyResult(
                    evidence,
                    "runtime-post-commit-outbox")),
            await PublishAsync(
                output,
                provenance,
                GroundworkLedgerObligation.RestartScenario(
                    "runtime-checkpoint-commit",
                    "dispose-and-reopen-same-database",
                    "runtime-checkpoint-bundle-process-restart"),
                captures,
                evidence => RuntimeProviderEvidenceScenarios.CreateCheckpointProcessRestartResult(
                    evidence,
                    "runtime-checkpoint-commit")),
            await PublishAsync(
                output,
                provenance,
                GroundworkLedgerObligation.RestartScenario(
                    "runtime-checkpoint-commit",
                    "process-restart-same-database",
                    "runtime-checkpoint-bundle-process-restart"),
                captures,
                evidence => RuntimeProviderEvidenceScenarios.CreateCheckpointProcessRestartResult(
                    evidence,
                    "runtime-checkpoint-commit"))
        };

        foreach (var failureWindow in new[]
                 {
                     "before-provider-decision",
                     "during-provider-decision",
                     "after-durable-decision-before-caller-acknowledgement"
                 })
        {
            publications.Add(await PublishAsync(
                output,
                provenance,
                GroundworkLedgerObligation.FailureWindow(
                    "runtime-checkpoint-commit",
                    failureWindow,
                    "runtime-checkpoint-failure-recovery"),
                captures,
                evidence => RuntimeProviderEvidenceScenarios.CreateCheckpointFailureRecoveryResult(
                    evidence,
                    "runtime-checkpoint-commit",
                    failureWindow)));
        }

        Assert.All(publications, publication => Assert.Equal(4, publication.LedgerRecords.Count));
        await GroundworkProviderEvidencePublisher.WriteVersionedLedgerAttachmentAsync(
            output,
            EvidenceVersion,
            "runtime-checkpoint-fence",
            publications);
    }

    private static Task<GroundworkProviderEvidencePublication> PublishAsync(
        string output,
        GroundworkProviderEvidenceProvenance provenance,
        GroundworkLedgerObligation obligation,
        IEnumerable<RuntimeCheckpointFenceExecutionResult> captures,
        Func<RuntimeCheckpointFenceExecutionResult, GroundworkScenarioResult> createResult) =>
        GroundworkProviderEvidencePublisher.PublishVersionedAsync(
            output,
            EvidenceVersion,
            provenance,
            obligation,
            captures.Select(createResult));

    private static GroundworkProviderEvidenceProvenance RequireVersionedPublicationProvenance()
    {
        var commit = Environment.GetEnvironmentVariable(SourceCommit);
        var tree = Environment.GetEnvironmentVariable(SourceTree);
        var runIdentity = Environment.GetEnvironmentVariable(RunIdentity);
        if (string.IsNullOrWhiteSpace(commit) ||
            string.IsNullOrWhiteSpace(tree) ||
            string.IsNullOrWhiteSpace(runIdentity))
        {
            throw new InvalidOperationException(
                $"Set {SourceCommit}, {SourceTree}, and {RunIdentity} to exact retained publication provenance.");
        }

        return GroundworkProviderEvidenceProvenance.Create(commit, tree, runIdentity);
    }

    private static string Observation(GroundworkScenarioResult result, string name) =>
        result.Observations.Single(observation => string.Equals(observation.Name, name, StringComparison.Ordinal)).Value;

    private static void RequirePublicationOptIn()
    {
        Skip.If(
            !string.Equals(Environment.GetEnvironmentVariable(PublishOptIn), "1", StringComparison.Ordinal),
            $"Set {PublishOptIn}=1 to publish the catalog-validated runtime provider-evidence slice.");
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EvidenceOutput)),
            $"Set {EvidenceOutput} to an explicit artifact output directory before publication.");
    }
}
