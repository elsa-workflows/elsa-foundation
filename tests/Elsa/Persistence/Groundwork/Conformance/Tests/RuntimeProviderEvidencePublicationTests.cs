using Elsa.Persistence.Groundwork.Testing;
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
    private const string SchedulerScenario = "runtime-scheduler-state-roundtrip";

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
        var captures = new List<RuntimeCheckpointFenceEvidence>();

        foreach (var providerKey in GroundworkStoreScenarioCatalog.MandatoryProviderKeys)
            captures.Add(await RuntimeProviderEvidenceScenarios.CaptureCheckpointFenceEvidenceAsync(providerKey));

        var publications = new List<GroundworkProviderEvidencePublication>
        {
            await PublishAsync(
                output,
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
        await GroundworkProviderEvidencePublisher.WriteLedgerAttachmentAsync(output, "runtime-checkpoint-fence", publications);
    }

    private static Task<GroundworkProviderEvidencePublication> PublishAsync(
        string output,
        GroundworkLedgerObligation obligation,
        IEnumerable<RuntimeCheckpointFenceEvidence> captures,
        Func<RuntimeCheckpointFenceEvidence, GroundworkScenarioResult> createResult) =>
        GroundworkProviderEvidencePublisher.PublishAsync(output, obligation, captures.Select(createResult));

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
