using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

/// <summary>
/// Direct red tests for the Spec 094 T019 provider-capability boundary. Provider support and
/// evidence describe what a package can do; only a matching active composition path can make that
/// capability available to an Elsa store.
/// </summary>
public class ProviderCapabilityContractTests
{
    private static readonly CapabilityId AtomicCommit = WellKnownCapabilities.AtomicCommit;

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
}
