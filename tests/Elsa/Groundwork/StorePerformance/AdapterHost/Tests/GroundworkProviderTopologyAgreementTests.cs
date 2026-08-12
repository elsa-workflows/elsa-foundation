using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Persistence.Groundwork.Testing;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

/// <summary>
/// Binds the workload catalog's declared provider evidence to the real driver topology vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// This exists because a keys-only check let an impossible contract stay green. The diagnostics workload
/// declared topologies like <c>file-backed-distinct-connections-with-retained-ef-oracle</c> — strings that
/// <see cref="GroundworkProviderTopology"/> rejects outright, so no driver could report them and no
/// diagnostics run could start on any provider. Every existing assertion passed throughout, because they
/// checked that the four provider keys were present and that each value was non-empty. See
/// <c>specs/094-harden-groundwork-stores/contracts/diagnostics-provider-topology-basis.md</c>.
/// </para>
/// <para>
/// The test deliberately probes the real constructor rather than reading a copy of the catalog. Comparing
/// two lists would only prove that two lists match; constructing the actual type proves the declared value
/// would survive the gate a driver and <c>MatrixPlan.Create</c> put it through. It is also why this lives
/// in the adapter-host suite: this is the only project that can see both the workload catalog and the
/// driver topology type.
/// </para>
/// </remarks>
public sealed class GroundworkProviderTopologyAgreementTests
{
    private readonly WorkloadCatalog _catalog = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot());

    [Fact]
    public void Every_workload_declares_provider_evidence_a_driver_could_actually_report()
    {
        Assert.NotEmpty(_catalog.Workloads);

        foreach (var (workloadId, workload) in _catalog.Workloads)
        {
            Assert.NotEmpty(workload.RequiredProviderEvidence);

            foreach (var (provider, topology) in workload.RequiredProviderEvidence)
            {
                // Capabilities are irrelevant to the identity check the constructor performs, so any value
                // is admissible here; what is under test is the provider/topology pair.
                var exception = Record.Exception(() => new GroundworkProviderTopology(
                    provider,
                    topology,
                    GroundworkTopologyCapabilities.None));

                Assert.True(
                    exception is null,
                    $"Workload '{workloadId}' declares provider evidence '{topology}' for provider '{provider}', " +
                    $"which no driver can report: {exception?.Message}. This field is consumed as a topology " +
                    "identifier by MatrixPlan.Create; gate-regime prose belongs in correctness.timingGate.");
            }
        }
    }

    /// <summary>
    /// Proves the catalog's own fail-closed check is the one doing the work, not an accident of the current
    /// JSON. Without this, the loader's guard could be deleted and the test above would still pass on a
    /// tree whose workloads happen to be correct.
    /// </summary>
    [Fact]
    public void The_catalog_rejects_a_topology_no_driver_could_report()
    {
        var rejected = Record.Exception(() => new GroundworkProviderTopology(
            "sqlite",
            "file-backed-distinct-connections-with-retained-ef-oracle",
            GroundworkTopologyCapabilities.None));

        Assert.NotNull(rejected);
        Assert.IsType<ArgumentException>(rejected);
    }
}
