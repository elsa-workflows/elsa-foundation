using Xunit;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Persistence.Schema;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Since #2143 the reader of schema-family declarations and the finalization record's model live in
/// <c>Elsa.Persistence.Schema</c>, which references no EF Core so every host can share it, while the declaration types and
/// the column mappings stay here. The catalog names the declaration types it matches rather than referencing them; a
/// name that drifted from its type would find no family at all, and a family missing from a host's readability report lets
/// a version finalize that the host cannot read. The catalog's own tests read synthetic declarations built with these same
/// types; these read this assembly's.
/// </summary>
public sealed class EfSchemaFamilyCatalogAcrossAssembliesTests
{
    [Fact]
    public void The_catalog_reads_this_assemblys_own_family_declaration_and_its_content_columns()
    {
        var families = EfSchemaFamilyCatalog.Discover([typeof(EfSchemaFinalization).Assembly]);
        var family = Assert.Single(families, x => x.Name == EfSchemaFinalization.SchemaFamily);
        Assert.Contains(families, x => x.Name == EfSourceModel.SchemaFamily && x.CurrentVersion == EfSourceModel.SchemaVersion && x.Module is null);

        Assert.Equal((EfSchemaFinalization.SchemaFamily, (string?)null, EfSchemaFinalization.SchemaVersion), (family.Name, family.Module, family.CurrentVersion));
        Assert.NotEmpty(family.ContentColumns);
    }

    /// <summary>The record's model refuses a version label wider than the stamp column this assembly maps.</summary>
    [Fact]
    public void The_model_refuses_a_version_wider_than_a_stamp_column()
    {
        SchemaVersionChain.Validate([new string('9', EfSchemaFinalization.MaxVersionLength)]);
        Assert.Throws<ArgumentException>(() => SchemaVersionChain.Validate([new string('9', EfSchemaFinalization.MaxVersionLength + 1)]));
    }
}
