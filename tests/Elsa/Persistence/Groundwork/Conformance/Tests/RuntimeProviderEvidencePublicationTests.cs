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
