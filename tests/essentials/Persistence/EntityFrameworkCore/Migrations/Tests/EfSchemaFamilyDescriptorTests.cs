using Elsa.Persistence.EntityFramework;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Every first-party module's <c>[EfSchemaFamily]</c> declarations (spec 180, FR-001) are discoverable the way a host's
/// readability source discovers them: each family is declared once across the tree, and owned by an <c>[EfModule]</c>
/// its own assembly declares. <c>EfSchemaFamilyDeclarationGuardTests</c> holds each declaration to the families the
/// stores check and the versions they check them at.
/// </summary>
public sealed class EfSchemaFamilyDescriptorTests
{
    [Fact]
    public void Every_first_party_family_is_declared_once_and_owned_by_a_module_of_its_own_assembly()
    {
        var modules = EfModuleCatalog.Discover(ModuleContextCatalog.Modules);
        var families = EfSchemaFamilyCatalog.Discover(ModuleContextCatalog.Modules);

        Assert.True(families.Count >= 26, $"Expected the twenty-six families every EF content table stamps since #2119 at least; found {families.Count}.");
        Assert.Equal(families.Count, families.Select(family => family.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(families, family => Assert.Contains(modules, module => module.Name == family.Module && module.Assembly == family.Assembly));
    }
}
