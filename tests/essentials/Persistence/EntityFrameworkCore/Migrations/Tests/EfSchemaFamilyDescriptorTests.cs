using System.Reflection;
using Elsa.Persistence.EntityFramework;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Every first-party module's <c>[EfSchemaFamily]</c> declarations (spec 180, FR-001) are discoverable the way a host's
/// readability source discovers them: each family is declared once across the tree, and owned by an <c>[EfModule]</c>
/// its own assembly declares. Each resolves to the chain its stores read through, which reads exactly what the host
/// reports. <c>EfSchemaFamilyDeclarationGuardTests</c> holds the source to the same rules.
/// </summary>
public sealed class EfSchemaFamilyDescriptorTests
{
    private static readonly Assembly[] Declaring = [.. ModuleContextCatalog.Modules, typeof(EfSchemaFinalization).Assembly];

    [Fact]
    public void Every_first_party_family_is_declared_once_and_owned_by_a_module_of_its_own_assembly()
    {
        var modules = EfModuleCatalog.Discover(ModuleContextCatalog.Modules);
        var families = EfSchemaFamilyCatalog.Discover(ModuleContextCatalog.Modules);

        Assert.True(families.Count >= 26, $"Expected the twenty-six families every EF content table stamps since #2119 at least; found {families.Count}.");
        Assert.Equal(families.Count, families.Select(family => family.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(families, family => Assert.Contains(modules, module => module.Name == family.Module && module.Assembly == family.Assembly));
    }

    /// <summary>
    /// FR-005 and FR-010: every family's registration accepts its chain, and the chain its stores read through is the one
    /// its module class holds, reading exactly the versions the host reports for it.
    /// </summary>
    [Fact]
    public void Every_first_party_family_reads_through_one_sound_chain_that_reads_what_the_host_reports()
    {
        var families = EfSchemaFamilyCatalog.Discover(Declaring);
        var handles = Declaring
            .SelectMany(assembly => assembly.GetTypes())
            .Select(type => type.GetField("Chain", BindingFlags.Public | BindingFlags.Static))
            .Where(field => field?.FieldType == typeof(EfSchemaChain))
            .Select(field => (EfSchemaChain)field!.GetValue(null)!)
            .ToDictionary(chain => chain.Family, StringComparer.Ordinal);

        Assert.All(families, family =>
        {
            var chain = EfSchemaChain.Of(family.Assembly, family.Name);
            chain.EnsureSound();
            Assert.Same(chain, handles[family.Name]);
            Assert.Equal(family.ReadableVersions, chain.ReadableVersions);
            Assert.Equal(family.CurrentVersion, chain.CurrentVersion);
        });
        Assert.Equal(families.Count, handles.Count);
    }

    /// <summary>
    /// FR-023 at the one place every store's check now goes through: for every family, a stamp past the current version,
    /// one below the chain and none at all are skew, never corruption. Every first-party family has one version, so any
    /// other label is both past and below it.
    /// </summary>
    [Theory]
    [InlineData("99.0.0")]
    [InlineData("0")]
    [InlineData(null)]
    public void Every_first_party_family_refuses_a_stamp_outside_its_chain_as_skew(string? stamp) =>
        Assert.All(EfSchemaFamilyCatalog.Discover(Declaring), family =>
        {
            var chain = EfSchemaChain.Of(family.Assembly, family.Name);
            var skew = Assert.Throws<EfSchemaVersionSkewException>(() => chain.Upcast(stamp, "any", "Content", "not-json"));
            Assert.Equal(family.Name, skew.Family);
            Assert.Equal(chain.ReadableVersions, skew.ReadableVersions);
        });
}
