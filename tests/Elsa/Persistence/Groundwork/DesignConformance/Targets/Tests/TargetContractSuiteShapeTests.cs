using System.Reflection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Targets.Tests;

/// <summary>
/// Shape guard for the Groundwork-aware shared conformance contracts.
/// <para>
/// The neutral contract project next door is guarded Groundwork-free, and this project exists because the
/// target-declaration contract cannot be written that way. That exemption is deliberately narrow: it buys
/// the right to name Groundwork, not the right to name a provider. A shared contract that reached SQLite
/// or SQL Server would stop being a contract and become one provider's test, and the other three leaves
/// would inherit assertions they cannot satisfy.
/// </para>
/// </summary>
public sealed class TargetContractSuiteShapeTests
{
    /// <summary>
    /// The concrete Groundwork provider leaves. A shared contract may know none of them; the per-provider
    /// conformance projects compose them and supply them to the suite through its abstract members.
    /// </summary>
    private static readonly string[] ProviderAssemblyMarkers =
    [
        "Groundwork.Sqlite",
        "Groundwork.SqlServer",
        "Groundwork.PostgreSql",
        "Groundwork.MongoDb"
    ];

    [Fact]
    public void Shared_target_contracts_are_abstract()
    {
        // A shared contract that is instantiable would run here, unbound to any provider, and either pass
        // vacuously or fail for reasons no leaf owns.
        Assert.True(typeof(GroundworkTargetConflictContractSuite).IsAbstract);
    }

    [Fact]
    public void Shared_target_contracts_name_no_concrete_provider()
    {
        var referenced = Assembly.GetExecutingAssembly()
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .ToArray();

        var providerReferences = referenced
            .Where(name => ProviderAssemblyMarkers.Any(marker =>
                name.Contains(marker, StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            providerReferences.Length == 0,
            "A shared Groundwork conformance contract referenced a concrete provider: " +
            string.Join(", ", providerReferences));
    }

    /// <summary>
    /// Negative control: the detector must actually fire, or the assertion above passes for the wrong
    /// reason once someone renames a provider assembly.
    /// </summary>
    [Fact]
    public void Provider_detection_flags_a_synthetic_provider_reference()
    {
        string[] synthetic = ["Elsa.Persistence.Groundwork.Sqlite", "Elsa.Persistence.Groundwork"];

        var flagged = synthetic
            .Where(name => ProviderAssemblyMarkers.Any(marker =>
                name.Contains(marker, StringComparison.Ordinal)))
            .ToArray();

        Assert.Equal(["Elsa.Persistence.Groundwork.Sqlite"], flagged);
    }
}
