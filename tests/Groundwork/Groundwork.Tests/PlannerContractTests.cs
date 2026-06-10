using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.Validation;
using Groundwork.Core.Workloads;
using Groundwork.Documents.Planning;
using Groundwork.Relational.Planning;
using Xunit;

namespace Groundwork.Tests;

public sealed class PlannerContractTests
{
    private readonly StorageManifestValidator _manifestValidator = new();
    private readonly ProviderCapabilityValidator _capabilityValidator = new();

    [Fact]
    public void SameManifestProducesRelationalAndDocumentPlans()
    {
        var manifest = SampleManifests.MetadataManifest();
        var capabilities = SampleManifests.PortableCapabilities();

        var relational = NewRelationalPlanner().Plan(manifest, capabilities);
        var document = NewDocumentPlanner().Plan(manifest, capabilities);

        Assert.True(relational.IsPlannable);
        Assert.True(document.IsPlannable);
        Assert.Single(relational.Tables);
        Assert.Single(document.Documents);
    }

    [Fact]
    public void RelationalPlanPreservesIndexesAndSchemaHistory()
    {
        var plan = NewRelationalPlanner().Plan(SampleManifests.MetadataManifest(), SampleManifests.PortableCapabilities());

        var table = Assert.Single(plan.Tables);
        Assert.Equal(["by-key", "by-category"], table.Indexes.Select(index => index.Name));
        Assert.Contains(plan.Operations, operation => operation.Kind == MaterializationOperationKind.RecordSchemaHistory);
        Assert.Equal("configuration.documents", plan.SchemaHistory.ManifestIdentity.Value);
    }

    [Fact]
    public void DocumentPlanPreservesIndexesQueriesAndSchemaHistory()
    {
        var plan = NewDocumentPlanner().Plan(SampleManifests.MetadataManifest(), SampleManifests.PortableCapabilities());

        var document = Assert.Single(plan.Documents);
        Assert.Equal(["by-key", "by-category"], document.Indexes.Select(index => index.Name));
        Assert.Equal(["find-by-key", "list-by-category"], document.Queries.Select(query => query.Name));
        Assert.Contains(plan.Operations, operation => operation.Kind == MaterializationOperationKind.RecordSchemaHistory);
        Assert.Equal("configuration.documents", plan.SchemaHistory.ManifestIdentity.Value);
    }

    [Fact]
    public void DocumentPlanAddsOptimizedProjectionOperationsForOptimizedUnits()
    {
        var manifest = SampleManifests.MetadataManifest();
        var optimizedUnit = manifest.StorageUnits.Single() with
        {
            Physicalization = PhysicalizationPolicy.Optimized
        };

        var plan = NewDocumentPlanner().Plan(
            manifest with { StorageUnits = [optimizedUnit] },
            SampleManifests.PortableCapabilities());

        Assert.Contains(plan.Operations, operation =>
            operation.Kind == MaterializationOperationKind.CreateOptimizedProjection &&
            operation.Target == "configurationDocument.by-key");
    }

    [Fact]
    public void UnsupportedWorkloadBlocksPlanning()
    {
        var capabilities = SampleManifests.PortableCapabilities() with
        {
            SupportedWorkloads = new HashSet<WorkloadFamily> { WorkloadFamily.CatalogAuthoredData }
        };

        var relational = NewRelationalPlanner().Plan(SampleManifests.MetadataManifest(), capabilities);
        var document = NewDocumentPlanner().Plan(SampleManifests.MetadataManifest(), capabilities);

        Assert.False(relational.IsPlannable);
        Assert.False(document.IsPlannable);
        Assert.Contains(relational.Diagnostics, diagnostic => diagnostic.Code == "GW-CAP-003");
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "GW-CAP-003");
    }

    private RelationalManifestPlanner NewRelationalPlanner() => new(_manifestValidator, _capabilityValidator);

    private DocumentManifestPlanner NewDocumentPlanner() => new(_manifestValidator, _capabilityValidator);
}
