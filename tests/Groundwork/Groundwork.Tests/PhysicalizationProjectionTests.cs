using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.Physicalization;
using Groundwork.Relational.Physicalization;
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

    [Fact]
    public void PhysicalizedProviderNamesDoNotCollideForDistinctIndexIdentities()
    {
        var unit = SampleManifests.MetadataManifest().StorageUnits.Single();
        var first = new PhysicalizedFieldPlan("by-key", "key", IndexValueKind.Keyword, false, true);
        var second = new PhysicalizedFieldPlan("by_key", "key", IndexValueKind.Keyword, false, true);
        var third = new PhysicalizedFieldPlan("ByKey", "key", IndexValueKind.Keyword, false, true);

        var names = new[]
        {
            RelationalPhysicalizationNames.ColumnName(first),
            RelationalPhysicalizationNames.ColumnName(second),
            RelationalPhysicalizationNames.ColumnName(third),
            RelationalPhysicalizationNames.IndexName(unit, first, false),
            RelationalPhysicalizationNames.IndexName(unit, second, false),
            RelationalPhysicalizationNames.IndexName(unit, third, false)
        };

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void PhysicalizedRelationalNamesStayWithinPostgreSqlIdentifierLimit()
    {
        var unit = SampleManifests.MetadataManifest().StorageUnits.Single() with
        {
            Identity = new StorageUnitIdentity("runtimeEntityInstanceWithAnIntentionallyLongIdentityThatWouldOverflowProviderLimits")
        };
        var field = new PhysicalizedFieldPlan(
            "byFieldWithUppercaseAndSeparators.ThatWouldOtherwiseProduceAnOversizedEncodedIdentifier",
            "key",
            IndexValueKind.Keyword,
            false,
            true);

        var names = new[]
        {
            RelationalPhysicalizationNames.TableName(unit),
            RelationalPhysicalizationNames.ColumnName(field),
            RelationalPhysicalizationNames.IndexName(unit, field, false),
            RelationalPhysicalizationNames.IndexName(unit, field, true)
        };

        Assert.All(names, name => Assert.True(name.Length <= 63, $"{name} exceeded the PostgreSQL identifier limit."));
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }
}
