using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.Physicalization;
using Xunit;

namespace Groundwork.Tests;

public sealed class PhysicalizationProjectionTests
{
    [Fact]
    public void PortableUnitsDoNotProducePhysicalizedFields()
    {
        var unit = SampleManifests.MetadataManifest().StorageUnits.Single();

        var fields = PhysicalizationProjection.EligibleFields(unit);

        Assert.Empty(fields);
    }

    [Fact]
    public void OptimizedUnitsProduceSingleFieldEqualityProjections()
    {
        var unit = SampleManifests.MetadataManifest().StorageUnits.Single() with
        {
            Physicalization = PhysicalizationPolicy.Optimized
        };

        var fields = PhysicalizationProjection.EligibleFields(unit);

        Assert.Equal(["by-key", "by-category"], fields.Select(field => field.Name));
    }

    [Fact]
    public void CompoundIndexesAreNotEligibleForG7Physicalization()
    {
        var unit = SampleManifests.MetadataManifest().StorageUnits.Single();
        var compoundIndex = new IndexDeclaration(
            "by-compound",
            [new IndexField("key"), new IndexField("category")],
            IndexValueKind.Keyword,
            false,
            true,
            MissingValueBehavior.Excluded,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal });
        var optimized = unit with
        {
            Physicalization = PhysicalizationPolicy.Optimized,
            Indexes = [.. unit.Indexes, compoundIndex]
        };

        var fields = PhysicalizationProjection.EligibleFields(optimized);

        Assert.DoesNotContain(fields, field => field.Name == "by-compound");
    }
}
