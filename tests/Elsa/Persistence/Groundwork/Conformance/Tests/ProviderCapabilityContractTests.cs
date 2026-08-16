using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

/// <summary>
/// Direct tests for the Spec 094 T092 provider-capability boundary. Provider support and
/// evidence describe what a package can do; only a matching active composition path can make that
/// capability available to an Elsa store.
/// </summary>
public class ProviderCapabilityContractTests
{
    private static readonly CapabilityId AtomicCommit = WellKnownCapabilities.AtomicCommit;
    private static readonly GroundworkActiveStoragePath CheckpointCommitPath = CreateCheckpointCommitPath();

    [Fact]
    public void Capability_is_available_when_support_evidence_and_the_active_path_intersect()
    {
        var path = AtomicPath();
        var snapshot = CreateSnapshot(
            activePaths: [path],
            supported: new HashSet<CapabilityId> { AtomicCommit },
            evidenced: new HashSet<CapabilityId> { AtomicCommit });

        Assert.True(snapshot.IsCapabilityAvailable(path, AtomicCommit));
    }

    [Fact]
    public void Provider_support_and_evidence_do_not_create_a_capability_without_an_active_path()
    {
        var snapshot = CreateSnapshot(
            activePaths: [],
            supported: new HashSet<CapabilityId> { AtomicCommit },
            evidenced: new HashSet<CapabilityId> { AtomicCommit });

        Assert.False(snapshot.IsCapabilityAvailable(AtomicPath(), AtomicCommit));
    }

    [Fact]
    public void An_active_supported_path_does_not_create_an_unevidenced_capability()
    {
        var path = AtomicPath();
        var snapshot = CreateSnapshot(
            activePaths: [path],
            supported: new HashSet<CapabilityId> { AtomicCommit },
            evidenced: new HashSet<CapabilityId>());

        Assert.False(snapshot.IsCapabilityAvailable(path, AtomicCommit));
    }

    [Fact]
    public void An_active_evidenced_path_does_not_create_an_unsupported_capability()
    {
        var path = AtomicPath();
        var snapshot = CreateSnapshot(
            activePaths: [path],
            supported: new HashSet<CapabilityId>(),
            evidenced: new HashSet<CapabilityId> { AtomicCommit });

        Assert.False(snapshot.IsCapabilityAvailable(path, AtomicCommit));
    }

    [Theory]
    [InlineData("other-feature", "unit", "atomic-route")]
    [InlineData("feature", "other-unit", "atomic-route")]
    [InlineData("feature", "unit", "other-route")]
    public void Capability_evidence_is_bound_to_the_exact_active_feature_unit_and_route(
        string featureIdentity,
        string storageUnitIdentity,
        string routeIdentity)
    {
        var snapshot = CreateSnapshot(
            activePaths: [AtomicPath()],
            supported: new HashSet<CapabilityId> { AtomicCommit },
            evidenced: new HashSet<CapabilityId> { AtomicCommit });
        var otherPath = new GroundworkActiveStoragePath(
            featureIdentity,
            new StorageUnitIdentity(storageUnitIdentity),
            routeIdentity,
            new HashSet<CapabilityId> { AtomicCommit });

        Assert.False(snapshot.IsCapabilityAvailable(otherPath, AtomicCommit));
    }

    [Fact]
    public void Topology_admission_uses_the_selected_provider_topology_not_configuration_intent()
    {
        var snapshot = CreateSnapshot(
            topologyCapabilities: new HashSet<string>(
                ["persistent-storage", "independent-clients"],
                StringComparer.Ordinal));

        Assert.True(snapshot.SupportsTopology(new GroundworkStorageTopologyRequirement("persistent-storage")));
        Assert.False(snapshot.SupportsTopology(new GroundworkStorageTopologyRequirement("multi-document-transactions")));
    }

    [Fact]
    public void Capability_derivation_catalog_is_closed_and_requires_the_mandatory_provider_topology()
    {
        var scenarioIds = GroundworkStoreScenarioCatalog
            .Requiring(GroundworkStoreScenarioEvidence.CapabilityDerivation)
            .Select(definition => definition.ScenarioId)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "distributed-bounded-route-equivalence",
                "distributed-command-lease-retry-and-stale-acknowledgement",
                "distributed-placement-claim-renew-and-takeover",
                "distributed-placement-stale-release-and-execution-fence",
                "iam-bounded-query-equivalence",
                "runtime-bounded-route-equivalence",
                "runtime-execution-ownership-fencing",
                "runtime-outbox-claim-retry-and-stale-acknowledgement",
                "runtime-recurring-schedule-conditional-advance",
                "runtime-scheduler-queue-claim-and-stale-acknowledgement",
                "runtime-timer-create-once-and-due-selection",
                "runtime-trigger-stimulus-lookup",
                "secret-bounded-list-equivalence"
            },
            scenarioIds);
        Assert.Equal(new[] { "mongodb", "postgresql", "sqlite", "sqlserver" }, GroundworkStoreScenarioCatalog.MandatoryProviderKeys);

        var scenarios = GroundworkStoreScenarioCatalog.Requiring(GroundworkStoreScenarioEvidence.CapabilityDerivation);
        foreach (var scenario in scenarios)
        {
            Assert.Equal(GroundworkStoreScenarioEvidenceSource.ProviderMatrix, scenario.EvidenceSource);
            Assert.True(scenario.RequiredEvidence.HasFlag(GroundworkStoreScenarioEvidence.PersistentStorage));
        }

        Assert.Contains(scenarios, scenario =>
            scenario.RequiredEvidence.HasFlag(GroundworkStoreScenarioEvidence.IndependentClients));
        Assert.Contains(scenarios, scenario =>
            !scenario.RequiredEvidence.HasFlag(GroundworkStoreScenarioEvidence.IndependentClients));
    }

    private static GroundworkProviderCapabilitySnapshot CreateSnapshot(
        IReadOnlyCollection<GroundworkActiveStoragePath>? activePaths = null,
        IReadOnlySet<CapabilityId>? supported = null,
        IReadOnlySet<CapabilityId>? evidenced = null,
        IReadOnlySet<string>? topologyCapabilities = null)
    {
        var provider = new ProviderIdentity("groundwork-sqlite", "1.0.0");
        var report = new ProviderCapabilityReport(
            provider,
            supported ?? new HashSet<CapabilityId> { AtomicCommit },
            evidenced ?? new HashSet<CapabilityId> { AtomicCommit },
            IndexCapabilities.All,
            Enum.GetValues<PortableQueryOperation>().ToHashSet(),
            Enum.GetValues<ConcurrencyKind>().ToHashSet(),
            []);
        var topology = new GroundworkProviderTopologySnapshot(
            provider.Name,
            "file-backed-distinct-connections",
            topologyCapabilities ?? new HashSet<string>(
                ["persistent-storage", "independent-clients"],
                StringComparer.Ordinal));
        return new GroundworkProviderCapabilitySnapshot(report, topology, activePaths ?? []);
    }

    private static GroundworkActiveStoragePath AtomicPath() => new(
        "feature",
        new StorageUnitIdentity("unit"),
        "atomic-route",
        new HashSet<CapabilityId> { AtomicCommit });

    private static GroundworkActiveStoragePath CreateCheckpointCommitPath()
    {
        var route = RuntimeGroundworkStorageManifestSource.CreateCheckpointCommitRouteRequirement();
        return new GroundworkActiveStoragePath(
            RuntimeGroundworkStorageManifestSource.FeatureName,
            route.StorageUnit,
            route.RouteIdentity,
            route.RequiredCapabilities);
    }

    public static IEnumerable<object[]> MandatoryProviderCapabilityReports()
    {
        yield return ["mongodb", MongoDbGroundworkCapabilities.RuntimeForTransactionCapableDeployment()];
        yield return ["postgresql", PostgreSqlGroundworkCapabilities.Runtime()];
        yield return ["sqlite", SqliteGroundworkCapabilities.Runtime()];
        yield return ["sqlserver", SqlServerGroundworkCapabilities.Runtime()];
    }
}
