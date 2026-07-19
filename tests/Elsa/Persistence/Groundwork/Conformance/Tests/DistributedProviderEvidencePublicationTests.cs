using Elsa.Persistence.Groundwork.Testing;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

/// <summary>
/// Opt-in publication entry point for the B9 obligations with executable, directly attributable evidence.
/// CAS races, failure windows, and restart remain unpublished until a catalog-validated runner proves them.
/// </summary>
public sealed class DistributedProviderEvidencePublicationTests
{
    private const string PublishOptIn = "ELSA_PUBLISH_GROUNDWORK_DISTRIBUTED_PLACEMENT_EVIDENCE";
    private const string EvidenceOutput = "ELSA_GROUNDWORK_EVIDENCE_OUTPUT";

    [SkippableFact]
    [Trait("Category", "GroundworkEvidencePublication")]
    public async Task Publish_the_real_distributed_ordinary_round_trip_matrices()
    {
        RequirePublicationOptIn();
        var output = Environment.GetEnvironmentVariable(EvidenceOutput)!;
        var publications = new List<GroundworkProviderEvidencePublication>();
        var placementResults = new List<GroundworkScenarioResult>();
        var commandResults = new List<GroundworkScenarioResult>();
        var boundedRouteResults = new Dictionary<DistributedNativeRoute, List<DistributedNativeRouteEvidence>>();

        foreach (var provider in GroundworkStoreScenarioCatalog.MandatoryProviderKeys)
        {
            placementResults.Add(await DistributedProviderEvidenceScenarios.RunPlacementClaimRenewAndTakeoverAsync(provider));
            commandResults.Add(await DistributedProviderEvidenceScenarios.RunCommandSendSequenceAsync(provider));
            foreach (var routeEvidence in await DistributedProviderEvidenceScenarios.CaptureBoundedRouteEvidenceAsync(provider))
            {
                if (!boundedRouteResults.TryGetValue(routeEvidence.Route, out var results))
                    boundedRouteResults.Add(routeEvidence.Route, results = []);
                results.Add(routeEvidence);
            }
        }

        GroundworkStoreScenarioCatalog.Get("distributed-placement-claim-renew-and-takeover")
            .RequireEquivalentProviderResults("distributed-execution-placement", placementResults);
        GroundworkStoreScenarioCatalog.Get("distributed-command-send-sequence")
            .RequireEquivalentProviderResults("distributed-command-transport", commandResults);
        var placementPublication = await GroundworkProviderEvidencePublisher.PublishAsync(
            output,
            GroundworkLedgerObligation.OrdinaryRoundTrip(
                "distributed-execution-placement",
                "distributed-placement-claim-renew-and-takeover"),
            placementResults);
        publications.Add(placementPublication);
        var commandPublication = await GroundworkProviderEvidencePublisher.PublishAsync(
            output,
            GroundworkLedgerObligation.OrdinaryRoundTrip(
                "distributed-command-transport",
                "distributed-command-send-sequence"),
            commandResults);
        publications.Add(commandPublication);
        var boundedRoutes = await PublishBoundedRoutesAsync(output, boundedRouteResults, publications);

        _ = await GroundworkProviderEvidencePublisher.WriteLedgerAttachmentAsync(
            output,
            "distributed-runtime",
            publications);

        Assert.Equal(4, placementPublication.LedgerRecords.Count);
        Assert.Equal(4, commandPublication.LedgerRecords.Count);
        Assert.Equal(16, boundedRoutes);
        Assert.All(
            placementPublication.LedgerRecords.Concat(commandPublication.LedgerRecords),
            record => Assert.Equal("ordinary-round-trip", record!["scenarioId"]!.GetValue<string>()));
    }

    private static async Task<int> PublishBoundedRoutesAsync(
        string output,
        IReadOnlyDictionary<DistributedNativeRoute, List<DistributedNativeRouteEvidence>> routes,
        ICollection<GroundworkProviderEvidencePublication> publications)
    {
        var count = 0;
        foreach (var (route, evidence) in routes)
        {
            var publication = await GroundworkProviderEvidencePublisher.PublishAsync(
                output,
                GroundworkLedgerObligation.QueryShape(
                    route.CoverageEntryId,
                    route.LedgerQueryShape,
                    route.NativeQueryIdentity,
                    "distributed-bounded-route-equivalence"),
                evidence.Select(item => item.Result),
                evidence.Select(item => item.Plan));
            publications.Add(publication);
            count += publication.LedgerRecords.Count;
        }

        return count;
    }

    private static void RequirePublicationOptIn()
    {
        Skip.If(
            !string.Equals(Environment.GetEnvironmentVariable(PublishOptIn), "1", StringComparison.Ordinal),
            $"Set {PublishOptIn}=1 to publish the real distributed ordinary-contract provider matrices.");
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EvidenceOutput)),
            $"Set {EvidenceOutput} to an explicit artifact output directory before publication.");
    }
}
