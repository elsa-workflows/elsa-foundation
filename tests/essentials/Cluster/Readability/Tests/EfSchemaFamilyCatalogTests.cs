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

    /// <summary>
    /// Shared mapping code - #2120's <c>SchemaFinalization</c> is the first case - has no single owning EF module. The
    /// two-argument declaration in an assembly with no <c>[EfModule]</c> of its own is accepted, and reads as
    /// <see cref="EfSchemaFamilyAttribute.SharedModule"/> rather than a module name.
    /// </summary>
    [Fact]
    public void A_shared_family_in_an_assembly_that_declares_no_module_is_accepted()
    {
        var mapping = Declare("Mapping", [], new SyntheticFamily("SchemaFinalization", null, "1"));

        var family = Assert.Single(EfSchemaFamilyCatalog.Discover([mapping]));

        Assert.Equal("SchemaFinalization", family.Name);
        Assert.Equal(EfSchemaFamilyAttribute.SharedModule, family.Module);
        Assert.Equal("1", family.CurrentVersion);
    }

    /// <summary>Several loaded copies of the same shared declaration are still two descriptors naming the same
    /// (shared) owner, exactly as two copies of an owned family are (combining them is the reader's decision).</summary>
    [Fact]
    public void Two_shared_declarations_of_the_same_family_are_both_discovered_naming_the_shared_owner()
    {
        var first = Declare("MappingA", [], new SyntheticFamily("SchemaFinalization", null, "1"));
        var second = Declare("MappingB", [], new SyntheticFamily("SchemaFinalization", null, "1"));

        var families = EfSchemaFamilyCatalog.Discover([first, second]);

        Assert.Equal(2, families.Count);
        Assert.All(families, family => Assert.Equal(EfSchemaFamilyAttribute.SharedModule, family.Module));
    }

    /// <summary>
    /// An assembly that owns an <c>[EfModule]</c> must name it as the family's owner; declaring the family shared
    /// instead would let it escape the "exactly one EF module" rule FR-001 states for an owned family.
    /// </summary>
    [Fact]
    public void A_shared_family_in_an_assembly_that_also_declares_a_module_is_refused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            EfSchemaFamilyCatalog.Discover([Declare("Sales", ["Sales"], new SyntheticFamily("Orders", null, "1"))]));

        Assert.Contains("shared", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'Sales'", refusal.Message, StringComparison.Ordinal);
    }

    public void Dispose() => _metadata.Dispose();

    private Assembly Declare(string assemblyName, string[] modules, params SyntheticFamily[] families) =>
        _metadata.Load(SyntheticSchemaFamilies.Image(assemblyName, modules, families));
}
