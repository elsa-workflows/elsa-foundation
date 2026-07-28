using Elsa.Persistence.Groundwork.Testing;
using System.Text.Json;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public class GroundworkStoreScenarioCatalogTests
{
    [Fact]
    public void Catalog_is_the_exact_sorted_all33_release_gate_denominator()
    {
        Assert.Equal(41, GroundworkStoreScenarioCatalog.All.Count);
        Assert.Equal(33, GroundworkStoreScenarioCatalog.CoverageEntryIds.Count);
        Assert.Equal(
            GroundworkStoreScenarioCatalog.CoverageEntryIds.Order(StringComparer.Ordinal),
            GroundworkStoreScenarioCatalog.CoverageEntryIds);
        Assert.Equal(
            GroundworkStoreScenarioCatalog.All.OrderBy(x => x.ScenarioId, StringComparer.Ordinal),
            GroundworkStoreScenarioCatalog.All);
        Assert.Equal(
            GroundworkStoreScenarioCatalog.CoverageEntryIds,
            GroundworkStoreScenarioCatalog.All
                .SelectMany(definition => definition.CoverageEntryIds)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            Enum.GetValues<GroundworkStoreScenarioFamily>().Order(),
            GroundworkStoreScenarioCatalog.All.Select(x => x.Family).Distinct().Order());
    }

    [Fact]
    public void Catalog_preserves_the_original_all33_floor_beside_the_two_diagnostics_rows()
    {
        using var ledger = JsonDocument.Parse(File.ReadAllText(FindRepositoryFile(
            "specs",
            "094-harden-groundwork-stores",
            "coverage-ledger.json")));
        var ledgerEntryIds = ledger.RootElement.GetProperty("entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("id").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            GroundworkStoreScenarioCatalog.CoverageEntryIds
                .Concat(["diagnostics-open-telemetry-store", "diagnostics-structured-log-store"])
                .Order(StringComparer.Ordinal),
            ledgerEntryIds);
    }

    [Fact]
    public void Catalog_separates_linked_diagnostics_evidence_from_local_provider_execution()
    {
        var linked = Assert.Single(
            GroundworkStoreScenarioCatalog.All,
            definition => definition.EvidenceSource is GroundworkStoreScenarioEvidenceSource.LinkedExternalOwner);

        Assert.Equal("runtime-diagnostics-linked-authority", linked.ScenarioId);
        Assert.Equal(["runtime-diagnostics-settings"], linked.CoverageEntryIds);
        Assert.Equal(GroundworkStoreScenarioEvidence.None, linked.RequiredEvidence);
        Assert.DoesNotContain(linked, GroundworkStoreScenarioCatalog.ForFamily(GroundworkStoreScenarioFamily.Runtime));
        Assert.Contains(
            linked,
            GroundworkStoreScenarioCatalog.ForFamily(
                GroundworkStoreScenarioFamily.Runtime,
                includeLinkedExternalOwner: true));
    }

    [Fact]
    public void Recovery_oracle_native_and_capability_lanes_are_explicit_catalog_views()
    {
        Assert.NotEmpty(GroundworkStoreScenarioCatalog.Requiring(GroundworkStoreScenarioEvidence.Cancellation));
        Assert.NotEmpty(GroundworkStoreScenarioCatalog.Requiring(GroundworkStoreScenarioEvidence.FailureInjection));
        Assert.NotEmpty(GroundworkStoreScenarioCatalog.Requiring(GroundworkStoreScenarioEvidence.ProcessRestart));
        Assert.NotEmpty(GroundworkStoreScenarioCatalog.Requiring(GroundworkStoreScenarioEvidence.NativeBoundedExecution));
        Assert.NotEmpty(GroundworkStoreScenarioCatalog.Requiring(GroundworkStoreScenarioEvidence.CapabilityDerivation));
        Assert.NotEmpty(GroundworkStoreScenarioCatalog.Requiring(GroundworkStoreScenarioEvidence.TemporaryEfOracle));

        Assert.All(
            GroundworkStoreScenarioCatalog.Requiring(GroundworkStoreScenarioEvidence.FailureInjection),
            definition => Assert.NotEmpty(definition.FailureWindows));
        Assert.All(
            GroundworkStoreScenarioCatalog.Requiring(GroundworkStoreScenarioEvidence.NativeBoundedExecution),
            definition => Assert.Contains(
                "native-execution",
                definition.ObservationNames));
    }

    [Fact]
    public void Exact_family_coverage_rejects_omissions_extras_and_duplicates()
    {
        var expected = GroundworkStoreScenarioCatalog.ForFamily(GroundworkStoreScenarioFamily.Secrets)
            .Select(definition => definition.ScenarioId)
            .ToArray();

        GroundworkStoreScenarioCatalog.RequireExactExecutionCoverage(
            GroundworkStoreScenarioFamily.Secrets,
            expected.Reverse());
        Assert.Throws<InvalidOperationException>(() =>
            GroundworkStoreScenarioCatalog.RequireExactExecutionCoverage(
                GroundworkStoreScenarioFamily.Secrets,
                expected.Skip(1)));
        Assert.Throws<InvalidOperationException>(() =>
            GroundworkStoreScenarioCatalog.RequireExactExecutionCoverage(
                GroundworkStoreScenarioFamily.Secrets,
                expected.Append("unexpected-scenario")));
        Assert.Throws<InvalidOperationException>(() =>
            GroundworkStoreScenarioCatalog.RequireExactExecutionCoverage(
                GroundworkStoreScenarioFamily.Secrets,
                expected.Append(expected[0])));
    }

    [Fact]
    public void Result_factory_enforces_closed_observations_clients_failure_windows_and_native_evidence()
    {
        var race = GroundworkStoreScenarioCatalog.Get("secret-concurrent-create-only");
        var failure = GroundworkStoreScenarioCatalog.Get("distributed-command-failure-and-restart");
        var bounded = GroundworkStoreScenarioCatalog.Get("secret-bounded-list-equivalence");
        var composition = GroundworkCompositionFingerprint.Create("scenario-catalog:v1");
        var provider = Descriptor(
            "sqlite",
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.ExternalProcessRestart |
            GroundworkTopologyCapabilities.MultiDocumentTransactions);

        Assert.Throws<ArgumentOutOfRangeException>(() => race.CreateResult(
            "secrets-repository",
            provider,
            composition,
            1,
            Observations(race)));
        Assert.Throws<InvalidOperationException>(() => race.CreateResult(
            "secrets-repository",
            provider,
            composition,
            2,
            Observations(race).Skip(1)));
        Assert.Throws<ArgumentException>(() => failure.CreateResult(
            "distributed-command-transport",
            provider,
            composition,
            2,
            Observations(failure)));
        Assert.Throws<ArgumentException>(() => bounded.CreateResult(
            "secrets-repository",
            provider,
            composition,
            1,
            Observations(bounded)));
    }

    [Fact]
    public void Result_digest_is_provider_independent_and_matrix_requires_all_four_equivalent_results()
    {
        var definition = GroundworkStoreScenarioCatalog.Get("secret-concurrent-create-only");
        var composition = GroundworkCompositionFingerprint.Create("scenario-catalog:v1");
        var observations = Observations(definition);
        var results = GroundworkStoreScenarioCatalog.MandatoryProviderKeys
            .Select(providerKey => definition.CreateResult(
                "secrets-repository",
                Descriptor(
                    providerKey,
                    GroundworkTopologyCapabilities.PersistentStorage |
                    GroundworkTopologyCapabilities.IndependentClients),
                composition,
                2,
                observations.Reverse()))
            .ToArray();

        definition.RequireEquivalentProviderResults("secrets-repository", results.Reverse());
        Assert.Single(results.Select(result => result.ResultDigest).Distinct(StringComparer.Ordinal));

        var divergent = definition.CreateResult(
            "secrets-repository",
            Descriptor(
                "mongodb",
                GroundworkTopologyCapabilities.PersistentStorage |
                GroundworkTopologyCapabilities.IndependentClients),
            composition,
            2,
            observations.Select(observation =>
                observation.Name == "winner-count"
                    ? new GroundworkScenarioObservation(observation.Name, "2")
                    : observation));
        Assert.Throws<InvalidOperationException>(() =>
            definition.RequireEquivalentProviderResults(
                "secrets-repository",
                results.Where(result => result.Provider.ProviderKey != "mongodb").Append(divergent)));
        Assert.Throws<InvalidOperationException>(() =>
            definition.RequireEquivalentProviderResults("secrets-repository", results.Skip(1)));

        var classifiedFailure = definition.CreateResult(
            "secrets-repository",
            Descriptor(
                "mongodb",
                GroundworkTopologyCapabilities.PersistentStorage |
                GroundworkTopologyCapabilities.IndependentClients),
            composition,
            2,
            observations,
            GroundworkScenarioOutcome.ClassifiedReadinessFailure);
        Assert.Throws<InvalidOperationException>(() =>
            definition.RequireEquivalentProviderResults(
                "secrets-repository",
                results.Where(result => result.Provider.ProviderKey != "mongodb").Append(classifiedFailure)));
    }

    private static IReadOnlyList<GroundworkScenarioObservation> Observations(
        GroundworkStoreScenarioDefinition definition) =>
        definition.ObservationNames
            .Select(name => new GroundworkScenarioObservation(name, $"observed-{name}"))
            .ToArray();

    private static GroundworkProviderDescriptor Descriptor(
        string providerKey,
        GroundworkTopologyCapabilities capabilities)
    {
        var (identity, topology) = providerKey switch
        {
            "sqlite" => ("groundwork-sqlite", "file-backed-distinct-connections"),
            "sqlserver" => ("groundwork-sqlserver", "real-sqlserver-container"),
            "postgresql" => ("groundwork-postgresql", "real-postgresql-container"),
            "mongodb" => ("groundwork-mongodb", "transaction-capable-replica-set"),
            _ => throw new ArgumentOutOfRangeException(nameof(providerKey))
        };
        return new GroundworkProviderDescriptor(
            providerKey,
            identity,
            "1.0.0",
            new GroundworkProviderTopology(providerKey, topology, capabilities));
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not locate repository file '{Path.Combine(segments)}'.");
    }
}
