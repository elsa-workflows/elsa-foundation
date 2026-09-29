using System.Reflection;
using Elsa.Persistence.Schema;

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

    /// <summary>
    /// FR-001 and FR-004: the chain is part of the declaration, read from metadata alone, each step's versions off its
    /// upcaster's own attribute, and the build reads the current version and every predecessor the chain reaches.
    /// </summary>
    [Fact]
    public void A_chain_is_read_from_metadata_alone()
    {
        var sales = Declare("Sales", ["Sales"], new SyntheticFamily("Orders", "Sales", "3",
            [new SyntheticUpcaster("Sales.OrdersOneToTwo", "1", "2"), new SyntheticUpcaster("Sales.OrdersTwoToThree", "2", "3")]));

        var family = Assert.Single(EfSchemaFamilyCatalog.Discover([sales]));

        Assert.Equal([("1", "2"), ("2", "3")], family.Upcasters.Select(upcaster => (upcaster.From, upcaster.To)));
        Assert.All(family.Upcasters, upcaster => Assert.Null(upcaster.Refusal));
        Assert.Equal(["1", "2", "3"], family.ReadableVersions);
        Assert.Empty(family.Defects);
    }

    /// <summary>
    /// A fault in the chain does not refuse discovery, which would drop every other family from the report; it hides the
    /// versions it leaves unreachable and is reported for the build and the family's registration to refuse.
    /// </summary>
    [Fact]
    public void A_chain_step_that_names_no_versions_hides_every_version_below_it()
    {
        var sales = Declare("Sales", ["Sales"], new SyntheticFamily("Orders", "Sales", "3",
            [new SyntheticUpcaster("Sales.OrdersOneToTwo", "1", "2"), new SyntheticUpcaster("Sales.OrdersUnnamed", null, null)]));

        var family = Assert.Single(EfSchemaFamilyCatalog.Discover([sales]));

        Assert.Equal(["3"], family.ReadableVersions);
        Assert.Contains(family.Defects, defect => defect.Contains("Sales.OrdersUnnamed", StringComparison.Ordinal));
    }

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
    /// <see langword="null"/> rather than a module name.
    /// </summary>
    [Fact]
    public void A_shared_family_in_an_assembly_that_declares_no_module_is_accepted()
    {
        var mapping = Declare("Mapping", [], new SyntheticFamily("SchemaFinalization", null, "1"));

        var family = Assert.Single(EfSchemaFamilyCatalog.Discover([mapping]));

        Assert.Equal("SchemaFinalization", family.Name);
        Assert.Null(family.Module);
        Assert.Equal("1", family.CurrentVersion);
    }

    /// <summary>Several loaded copies of the same shared declaration are still two descriptors naming the same
    /// (shared, null) owner, exactly as two copies of an owned family are (combining them is the reader's
    /// decision).</summary>
    [Fact]
    public void Two_shared_declarations_of_the_same_family_are_both_discovered_naming_the_shared_owner()
    {
        var first = Declare("MappingA", [], new SyntheticFamily("SchemaFinalization", null, "1"));
        var second = Declare("MappingB", [], new SyntheticFamily("SchemaFinalization", null, "1"));

        var families = EfSchemaFamilyCatalog.Discover([first, second]);

        Assert.Equal(2, families.Count);
        Assert.All(families, family => Assert.Null(family.Module));
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

    /// <summary>
    /// Spec 180, FR-009 and FR-008: a family's content and integrity columns are declared beside it and read, from metadata
    /// alone, onto its descriptor, which is what the guards and the model test hold every read and write to.
    /// </summary>
    [Fact]
    public void Content_and_integrity_columns_are_read_onto_their_family_from_metadata_alone()
    {
        var sales = Declare("Sales", ["Sales"],
            [
                new SyntheticColumn("Orders", "Sales.OrderRow", "ContentJson"),
                new SyntheticColumn("Orders", "Sales.OrderRow", "DigestJson", "Compared as stored bytes."),
                new SyntheticColumn("Invoices", "Sales.InvoiceRow", "PayloadJson")
            ],
            new SyntheticFamily("Orders", "Sales", "1"), new SyntheticFamily("Invoices", "Sales", "1"), new SyntheticFamily("Ledger", "Sales", "1"));

        var families = EfSchemaFamilyCatalog.Discover([sales]).ToDictionary(family => family.Name);

        Assert.Equal([("OrderRow", "ContentJson")], families["Orders"].ContentColumns.Select(column => (column.Entity.Name, column.Name)));
        Assert.Equal([("OrderRow", "DigestJson", "Compared as stored bytes.")], families["Orders"].IntegrityColumns.Select(column => (column.Entity.Name, column.Name, column.Reason)));
        Assert.Equal([("InvoiceRow", "PayloadJson")], families["Invoices"].ContentColumns.Select(column => (column.Entity.Name, column.Name)));
        Assert.Empty(families["Ledger"].ContentColumns);
        Assert.Empty(families["Ledger"].IntegrityColumns);
    }

    /// <summary>
    /// Spec 186, FR-004 and FR-010b: a family's content-addressed tables and its rewriter are read from the declaration's
    /// named arguments as metadata alone, by name, since the catalog names the attribute rather than referencing it; a
    /// family that names neither reads as none.
    /// </summary>
    [Fact]
    public void Content_addressed_tables_and_the_rewriter_are_read_onto_their_family_from_metadata_alone()
    {
        var sales = Declare("Sales", ["Sales"],
            new SyntheticFamily("Orders", "Sales", "1", ContentAddressed: ["Sales.ReceiptRow", "Sales.TemplateRow"], Rewriter: "Sales.OrdersRewriter"),
            new SyntheticFamily("Ledger", "Sales", "1"));

        var families = EfSchemaFamilyCatalog.Discover([sales]).ToDictionary(family => family.Name);

        Assert.Equal(["ReceiptRow", "TemplateRow"], families["Orders"].ContentAddressed.Select(type => type.Name));
        Assert.Equal("OrdersRewriter", families["Orders"].Rewriter?.Name);
        Assert.Empty(families["Ledger"].ContentAddressed);
        Assert.Null(families["Ledger"].Rewriter);
    }

    [Theory]
    [InlineData("a column of a family the assembly does not declare", "which that assembly does not declare")]
    [InlineData("a column declared twice", "2 times")]
    [InlineData("an integrity column with no reason", "with no reason")]
    [InlineData("a content declaration with a blank column", "no type or no column name")]
    public void A_column_declaration_the_catalog_cannot_hold_a_family_to_is_refused(string name, string expected)
    {
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            EfSchemaFamilyCatalog.Discover([Declare("Sales", ["Sales"], RefusedColumns[name], new SyntheticFamily("Orders", "Sales", "1"))]));

        Assert.True(refusal.Message.Contains(expected, StringComparison.Ordinal), $"'{name}' was refused for another reason: {refusal.Message}");
    }

    private static readonly Dictionary<string, SyntheticColumn[]> RefusedColumns = new(StringComparer.Ordinal)
    {
        ["a column of a family the assembly does not declare"] = [new SyntheticColumn("Invoices", "Sales.Row", "ContentJson")],
        ["a column declared twice"] = [new SyntheticColumn("Orders", "Sales.Row", "ContentJson"), new SyntheticColumn("Orders", "Sales.Row", "ContentJson", "Stored bytes.")],
        ["an integrity column with no reason"] = [new SyntheticColumn("Orders", "Sales.Row", "DigestJson", " ")],
        ["a content declaration with a blank column"] = [new SyntheticColumn("Orders", "Sales.Row", " ")]
    };

    public void Dispose() => _metadata.Dispose();

    private Assembly Declare(string assemblyName, string[] modules, params SyntheticFamily[] families) =>
        _metadata.Load(SyntheticSchemaFamilies.Image(assemblyName, modules, families));

    private Assembly Declare(string assemblyName, string[] modules, SyntheticColumn[] columns, params SyntheticFamily[] families) =>
        _metadata.Load(SyntheticSchemaFamilies.Image(assemblyName, modules, columns, families));
}
