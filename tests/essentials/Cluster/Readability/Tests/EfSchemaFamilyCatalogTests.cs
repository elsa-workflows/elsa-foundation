using System.Reflection;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Cluster.Readability.Tests;

/// <summary>
/// Spec 180's FR-001: a family declaration names the family, its owning EF module and the version this build reads, and
/// is readable as metadata without composing a shell or a container, or running any of the declaring assembly's code.
/// </summary>
public sealed class EfSchemaFamilyCatalogTests : IDisposable
{
    private readonly MetadataOnlyAssemblies _metadata = new();

    [Fact]
    public void A_declaration_is_read_from_metadata_alone()
    {
        var sales = Declare("Sales", ["Sales"], new SyntheticFamily("Orders", "Sales", "1.0.0"), new SyntheticFamily("Invoices", "Sales", "3"));

        var families = EfSchemaFamilyCatalog.Discover([sales]);

        Assert.Equal(
            [("Orders", "Sales", "1.0.0"), ("Invoices", "Sales", "3")],
            families.Select(family => (family.Name, family.Module, family.CurrentVersion)));
        Assert.All(families, family => Assert.Same(sales, family.Assembly));
    }

    [Fact]
    public void A_build_reads_exactly_its_current_version_until_the_chain_declares_predecessors() =>
        Assert.Equal(["3"], Assert.Single(EfSchemaFamilyCatalog.Discover([Declare("Sales", ["Sales"], new SyntheticFamily("Orders", "Sales", "3"))])).ReadableVersions);

    [Fact]
    public void The_owning_module_is_reported_as_its_EfModule_declaration_spells_it() =>
        Assert.Equal("Sales.Orders", Assert.Single(EfSchemaFamilyCatalog.Discover([Declare("Sales", ["Sales.Orders"], new SyntheticFamily("Orders", "sales.orders", "1"))])).Module);

    [Fact]
    public void A_family_owned_by_a_module_its_assembly_does_not_declare_is_refused()
    {
        var sales = Declare("Sales", ["Sales"], new SyntheticFamily("Orders", "Billing", "1"));

        var refusal = Assert.Throws<InvalidOperationException>(() => EfSchemaFamilyCatalog.Discover([sales]));

        Assert.Contains("'Billing'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'Sales'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_family_in_an_assembly_that_declares_no_module_is_refused() =>
        Assert.Contains(
            "declares none",
            Assert.Throws<InvalidOperationException>(() => EfSchemaFamilyCatalog.Discover([Declare("Loose", [], new SyntheticFamily("Orders", "Sales", "1"))])).Message,
            StringComparison.Ordinal);

    [Fact]
    public void A_family_one_assembly_declares_twice_is_refused() =>
        Assert.Contains(
            "'Orders' more than once",
            Assert.Throws<InvalidOperationException>(() => EfSchemaFamilyCatalog.Discover(
                [Declare("Sales", ["Sales"], new SyntheticFamily("Orders", "Sales", "1"), new SyntheticFamily("Orders", "Sales", "2"))])).Message,
            StringComparison.Ordinal);

    [Theory]
    [InlineData(" ", "1")]
    [InlineData("Orders", "")]
    public void A_declaration_without_a_name_or_a_version_is_refused(string family, string version) =>
        Assert.Throws<InvalidOperationException>(() => EfSchemaFamilyCatalog.Discover([Declare("Sales", ["Sales"], new SyntheticFamily(family, "Sales", version))]));

    [Fact]
    public void An_assembly_that_declares_no_family_contributes_nothing() =>
        Assert.Empty(EfSchemaFamilyCatalog.Discover([typeof(object).Assembly, Declare("Sales", ["Sales"])]));

    public void Dispose() => _metadata.Dispose();

    private Assembly Declare(string assemblyName, string[] modules, params SyntheticFamily[] families) =>
        _metadata.Load(SyntheticSchemaFamilies.Image(assemblyName, modules, families));
}
