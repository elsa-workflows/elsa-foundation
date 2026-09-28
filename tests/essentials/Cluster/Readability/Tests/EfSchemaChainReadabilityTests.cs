using System.Reflection;
using Elsa.Cluster.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Cluster.Readability.Tests;

/// <summary>
/// Spec 180's upcaster chain as a host reports it (spec 183, FR-019 to FR-021). The invariant: the versions a host
/// reports for a family are exactly the versions its reads of that family accept, and never one whose chain step is
/// missing, because B5's gate finalizes a version only when every member reports it.
/// </summary>
public sealed class EfSchemaChainReadabilityTests : IDisposable
{
    private static readonly Assembly Declaring = typeof(ChainedSchemaFamilies).Assembly;

    private readonly MetadataOnlyAssemblies _metadata = new();

    [Fact]
    public async Task A_host_reports_the_current_version_and_every_predecessor_its_chain_reaches()
    {
        var entry = Assert.Single((await new EfSchemaReadabilitySource().ReadAsync()).Entries, entry => entry.Family == ChainedSchemaFamilies.Orders);

        Assert.Equal(new ReadabilityEntry(ChainedSchemaFamilies.Orders, ChainedSchemaFamilies.Module, ["1", "2", "3"]), entry);
    }

    /// <summary>
    /// The report and the reads are computed from one declaration by one function, so for every version a host reports
    /// its reads accept that row, and for every version they refuse it reports nothing.
    /// </summary>
    [Theory]
    [InlineData(ChainedSchemaFamilies.Orders)]
    [InlineData(ChainedSchemaFamilies.Gapped)]
    public void The_versions_a_host_reports_are_exactly_the_versions_its_reads_accept(string family)
    {
        var reported = Assert.Single(EfSchemaReadabilitySource.Read(EfSchemaFamilyCatalog.Discover([Declaring])).Entries, entry => entry.Family == family).ReadableVersions;
        var chain = EfSchemaChain.Of(Declaring, family);

        Assert.Equal(chain.ReadableVersions, reported);
        foreach (var version in (string[])["0", "1", "2", "3", "4"])
            Assert.Equal(reported.Contains(version), chain.IsReadable(version));
    }

    /// <summary>
    /// A chain with no upcaster from 1 to 2 credits only what the current version reaches without the gap. A host
    /// crediting 1 would let a version finalize whose rows it refuses as skew.
    /// </summary>
    [Fact]
    public void A_version_whose_chain_step_is_missing_is_never_credited_and_the_fault_is_logged()
    {
        var logger = new RecordingLogger<EfSchemaReadabilitySource>();

        var entry = Assert.Single(EfSchemaReadabilitySource.Read(EfSchemaFamilyCatalog.Discover([Declaring]), logger).Entries, entry => entry.Family == ChainedSchemaFamilies.Gapped);

        Assert.Equal(["2", "3"], entry.ReadableVersions);
        var error = Assert.Single(logger.Entries, logged => logged.Level == LogLevel.Error);
        Assert.Contains(ChainedSchemaFamilies.Gapped, error.Message, StringComparison.Ordinal);
        Assert.Contains("gap", error.Message, StringComparison.Ordinal);
    }

    /// <summary>FR-005: the family's registration refuses a chain with a gap, while the host is still wiring itself up.</summary>
    [Fact]
    public void Registering_a_module_whose_family_has_a_gap_is_refused_at_startup()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddEfModuleMigrations<ChainedSchemaFamilies.Context>("Sqlite"));

        Assert.Contains($"'{ChainedSchemaFamilies.Gapped}'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("gap", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two generations of one package, the older at version 1 and the newer at 2 with an upcaster from 1: the newer one
    /// reads the older's rows, so the host reads 1, and only 1, however both are loaded.
    /// </summary>
    [Fact]
    public void Two_generations_report_the_version_both_read_once_the_newer_one_upcasts_the_older()
    {
        var older = Declare("Older", new SyntheticFamily("Orders", "Sales", "1"));
        var newer = Declare("Newer", new SyntheticFamily("Orders", "Sales", "2", [new SyntheticUpcaster("Sales.OrdersOneToTwo", "1", "2")]));

        var entry = Assert.Single(EfSchemaReadabilitySource.Read(EfSchemaFamilyCatalog.Discover([older, newer])).Entries);

        Assert.Equal(["1"], entry.ReadableVersions);
    }

    public void Dispose() => _metadata.Dispose();

    private Assembly Declare(string assemblyName, SyntheticFamily family) =>
        _metadata.Load(SyntheticSchemaFamilies.Image(assemblyName, [family.Module!], family));
}
