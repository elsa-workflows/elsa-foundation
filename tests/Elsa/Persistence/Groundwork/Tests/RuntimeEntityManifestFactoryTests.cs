using Elsa.Persistence.Groundwork.RuntimeEntities;
using Groundwork.Core.Indexing;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class RuntimeEntityManifestFactoryTests
{
    [Fact]
    public void CreateMapsRuntimeEntityDefinitionToDefinitionAndInstanceUnits()
    {
        var definition = CustomerProfileDefinition();

        var manifest = RuntimeEntityManifestFactory.Create(definition);

        Assert.Equal("elsa.runtime-entities.customerProfile", manifest.Identity.Value);
        Assert.Contains(manifest.StorageUnits, unit => unit.Identity.Value == RuntimeEntityManifestFactory.DefinitionDocumentKind);

        var instanceUnit = Assert.Single(manifest.StorageUnits, unit => unit.Identity.Value == RuntimeEntityManifestFactory.InstanceDocumentKind(definition));
        Assert.Contains(instanceUnit.Indexes, index => index.Identity == "by-email" && index.IsUnique);
        Assert.Contains(instanceUnit.Indexes, index => index.Identity == "by-segment" && !index.IsUnique);
    }

    internal static RuntimeEntityDefinition CustomerProfileDefinition() =>
        new(
            "customerProfile",
            "Customer Profile",
            [
                new RuntimeEntityIndex("by-email", "email", IndexValueKind.Keyword, IsUnique: true),
                new RuntimeEntityIndex("by-segment", "segment", IndexValueKind.Keyword)
            ],
            [
                new RuntimeEntityField("email", IndexValueKind.Keyword),
                new RuntimeEntityField("segment", IndexValueKind.Keyword)
            ]);
}
