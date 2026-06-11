using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;
using Groundwork.Core.Validation;
using Groundwork.Core.Workloads;
using Xunit;

namespace Groundwork.Tests;

public sealed class ManifestValidationTests
{
    private readonly StorageManifestValidator _validator = new();

    [Fact]
    public void ValidSampleManifestSucceeds()
    {
        var result = _validator.Validate(SampleManifests.MetadataManifest());

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void EmptyManifestFails()
    {
        var result = _validator.Validate(SampleManifests.MetadataManifest() with { StorageUnits = [] });

        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == "GW-MANIFEST-004");
    }

    [Fact]
    public void MissingWorkloadClassificationFails()
    {
        var manifest = WithSingleUnit(unit => unit with { Workload = null! });

        var result = _validator.Validate(manifest);

        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == "GW-UNIT-003");
    }

    [Fact]
    public void MissingTenancyPolicyFails()
    {
        var manifest = WithSingleUnit(unit => unit with { Tenancy = null! });

        var result = _validator.Validate(manifest);

        Assert.Contains(result.Errors, diagnostic =>
            diagnostic.Code == "GW-UNIT-011" &&
            diagnostic.Target == "manifest.storageUnits[0].tenancy");
    }

    [Fact]
    public void MissingSchemaVersionFails()
    {
        var result = _validator.Validate(SampleManifests.MetadataManifest() with { Version = new StorageManifestVersion("") });

        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == "GW-MANIFEST-003");
    }

    [Fact]
    public void QueryReferencingUndeclaredIndexFails()
    {
        var manifest = WithSingleUnit(unit => unit with
        {
            Queries =
            [
                new PortableQueryDeclaration(
                    "find-by-missing",
                    "missing-index",
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                    QuerySortSupport.None,
                    QueryPagingSupport.None)
            ]
        });

        var result = _validator.Validate(manifest);

        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == "GW-QUERY-002");
    }

    [Fact]
    public void CompoundIndexFailsUntilPortableSupportExists()
    {
        var manifest = WithSingleUnit(unit => unit with
        {
            Indexes =
            [
                new IndexDeclaration(
                    "by-key-and-category",
                    [new IndexField("key"), new IndexField("category")],
                    IndexValueKind.Keyword,
                    true,
                    true,
                    MissingValueBehavior.Excluded,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
            ]
        });

        var result = _validator.Validate(manifest);

        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == "GW-INDEX-006");
    }

    [Fact]
    public void ProviderSpecificRequiredPhysicalShapeFails()
    {
        var manifests = new[]
        {
            WithSingleUnit(unit => unit with { Identity = new StorageUnitIdentity("table:configuration_documents") }),
            WithSingleUnit(unit => unit with { Identity = new StorageUnitIdentity("postgresql:configuration_documents") })
        };

        foreach (var manifest in manifests)
        {
            var result = _validator.Validate(manifest);
            Assert.Contains(result.Errors, diagnostic => diagnostic.Code == "GW-UNIT-002");
        }
    }

    [Fact]
    public void OperationalWorkloadCannotUsePortableDefault()
    {
        var manifest = WithSingleUnit(unit => unit with
        {
            Workload = new WorkloadClassification(WorkloadFamily.OperationalStream, WorkloadCandidateCategory.GroundworkDefault)
        });

        var result = _validator.Validate(manifest);

        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == "GW-UNIT-004");
    }

    private static StorageManifest WithSingleUnit(Func<StorageUnit, StorageUnit> configure)
    {
        var manifest = SampleManifests.MetadataManifest();
        return manifest with { StorageUnits = [configure(manifest.StorageUnits.Single())] };
    }
}
