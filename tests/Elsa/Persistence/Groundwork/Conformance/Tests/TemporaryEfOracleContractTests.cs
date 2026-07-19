using Elsa.Persistence.Groundwork.Testing;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

/// <summary>
/// T091's boundary for the retired temporary EF oracle. The oracle is external comparison
/// evidence only: it has no executor, provider registration, or runtime composition role here.
/// </summary>
public sealed class TemporaryEfOracleContractTests
{
    [Fact]
    public void Temporary_oracle_contract_maps_exactly_the_catalog_scenarios_that_require_it()
    {
        var mapped = TemporaryEfOracleScenarioContract.All;
        var catalog = GroundworkStoreScenarioCatalog.Requiring(
            GroundworkStoreScenarioEvidence.TemporaryEfOracle);
        var expectedCoverage = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["identity-authority-adaptation"] = ["iam-external-identity", "iam-role", "iam-user"],
            ["runtime-ordinary-state-revision-and-reopen"] =
            [
                "runtime-activity-execution-inspection",
                "runtime-activity-execution-state",
                "runtime-bookmark-state",
                "runtime-durable-value-state",
                "runtime-executable-source-reference",
                "runtime-trigger-binding",
                "runtime-workflow-executable",
                "runtime-workflow-execution-state"
            ]
        };

        Assert.Equal(
            ["identity-authority-adaptation", "runtime-ordinary-state-revision-and-reopen"],
            mapped.Select(contract => contract.ScenarioId));
        Assert.Equal(
            catalog.Select(definition => definition.ScenarioId).Order(StringComparer.Ordinal),
            mapped.Select(contract => contract.ScenarioId));
        Assert.Equal(expectedCoverage.Keys.Order(StringComparer.Ordinal), mapped.Select(contract => contract.ScenarioId));
        Assert.All(
            mapped,
            contract =>
            {
                var definition = GroundworkStoreScenarioCatalog.Get(contract.ScenarioId);
                Assert.Equal(expectedCoverage[contract.ScenarioId], contract.CoverageEntryIds);
                Assert.Equal(definition.CoverageEntryIds, contract.CoverageEntryIds);
                Assert.Equal(definition.ObservationNames, contract.ObservationNames);
                Assert.Equal(definition.RequiredEvidence, contract.RequiredEvidence);
                Assert.True(definition.RequiredEvidence.HasFlag(GroundworkStoreScenarioEvidence.TemporaryEfOracle));
            });
    }

    [Fact]
    public void Temporary_oracle_comparison_uses_the_shared_provider_neutral_result_digest()
    {
        var composition = GroundworkCompositionFingerprint.Create("temporary-oracle-contract:v1");

        foreach (var contract in TemporaryEfOracleScenarioContract.All)
        {
            var sqlite = Result(contract, "sqlite", composition);
            var postgresql = Result(contract, "postgresql", composition);
            var evidence = TemporaryEfOracleComparisonEvidence.Create(
                contract,
                sqlite,
                oracleResultDigest: sqlite.ResultDigest);

            Assert.Equal(sqlite.ResultDigest, postgresql.ResultDigest);
            Assert.Equal(sqlite.ResultDigest, evidence.ProviderResultDigest);
            Assert.Equal(sqlite.ResultDigest, evidence.OracleResultDigest);
            Assert.Equal(contract.ScenarioId, evidence.ScenarioId);
            Assert.Equal("external-comparison-evidence", evidence.EvidenceRole);
            Assert.Equal("equivalent", evidence.Outcome);
            Assert.Throws<InvalidOperationException>(() =>
                TemporaryEfOracleComparisonEvidence.Create(
                    contract,
                    sqlite,
                    oracleResultDigest: new string('f', 64)));
        }
    }

    [Fact]
    public void Temporary_oracle_contract_has_no_runtime_or_provider_dependency()
    {
        Assert.All(
            TemporaryEfOracleScenarioContract.All,
            contract =>
            {
                Assert.False(contract.RequiresRuntimeDependency);
                Assert.False(contract.RequiresProviderDependency);
                Assert.Equal("external-comparison-evidence", contract.EvidenceRole);
            });

        var referencedAssemblies = typeof(TemporaryEfOracleContractTests)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();
        Assert.DoesNotContain(
            referencedAssemblies,
            name => name?.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) is true);
    }

    private static GroundworkScenarioResult Result(
        TemporaryEfOracleScenarioContract contract,
        string providerKey,
        GroundworkCompositionFingerprint composition) =>
        contract.Definition.CreateResult(
            contract.CoverageEntryIds[0],
            Provider(providerKey),
            composition,
            clients: 1,
            contract.ObservationNames.Select(name => new GroundworkScenarioObservation(name, $"observed-{name}")));

    private static GroundworkProviderDescriptor Provider(string providerKey) => providerKey switch
    {
        "sqlite" => new GroundworkProviderDescriptor(
            providerKey,
            "groundwork-sqlite",
            "comparison-only",
            new GroundworkProviderTopology(
                providerKey,
                "file-backed-distinct-connections",
                GroundworkTopologyCapabilities.PersistentStorage |
                GroundworkTopologyCapabilities.ExternalProcessRestart)),
        "postgresql" => new GroundworkProviderDescriptor(
            providerKey,
            "groundwork-postgresql",
            "comparison-only",
            new GroundworkProviderTopology(
                providerKey,
                "real-postgresql-container",
                GroundworkTopologyCapabilities.PersistentStorage |
                GroundworkTopologyCapabilities.ExternalProcessRestart)),
        _ => throw new ArgumentOutOfRangeException(nameof(providerKey), providerKey, null)
    };
}

internal sealed record TemporaryEfOracleScenarioContract(
    GroundworkStoreScenarioDefinition Definition)
{
    public const string ComparisonEvidenceRole = "external-comparison-evidence";

    public static IReadOnlyList<TemporaryEfOracleScenarioContract> All { get; } =
        GroundworkStoreScenarioCatalog.Requiring(GroundworkStoreScenarioEvidence.TemporaryEfOracle)
            .OrderBy(definition => definition.ScenarioId, StringComparer.Ordinal)
            .Select(definition => new TemporaryEfOracleScenarioContract(definition))
            .ToArray();

    public string ScenarioId => Definition.ScenarioId;
    public IReadOnlyList<string> CoverageEntryIds => Definition.CoverageEntryIds;
    public IReadOnlyList<string> ObservationNames => Definition.ObservationNames;
    public GroundworkStoreScenarioEvidence RequiredEvidence => Definition.RequiredEvidence;
    public string EvidenceRole => ComparisonEvidenceRole;
    public bool RequiresRuntimeDependency => false;
    public bool RequiresProviderDependency => false;
}

internal sealed record TemporaryEfOracleComparisonEvidence(
    string ScenarioId,
    string ProviderResultDigest,
    string OracleResultDigest,
    string Outcome,
    string EvidenceRole)
{
    public static TemporaryEfOracleComparisonEvidence Create(
        TemporaryEfOracleScenarioContract contract,
        GroundworkScenarioResult result,
        string oracleResultDigest)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(result);
        if (!string.Equals(contract.ScenarioId, result.ScenarioId, StringComparison.Ordinal) ||
            !contract.CoverageEntryIds.Contains(result.CoverageEntryId, StringComparer.Ordinal))
            throw new ArgumentException("The comparison result is outside the temporary-oracle scenario contract.", nameof(result));
        if (!string.Equals(result.ResultDigest, oracleResultDigest, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"The external temporary oracle result for scenario '{contract.ScenarioId}' is not equivalent to the Groundwork result.");

        return new TemporaryEfOracleComparisonEvidence(
            contract.ScenarioId,
            result.ResultDigest,
            oracleResultDigest,
            "equivalent",
            TemporaryEfOracleScenarioContract.ComparisonEvidenceRole);
    }
}
