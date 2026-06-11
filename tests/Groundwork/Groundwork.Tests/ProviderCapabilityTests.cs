using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.Validation;
using Xunit;

namespace Groundwork.Tests;

public sealed class ProviderCapabilityTests
{
    private readonly ProviderCapabilityValidator _validator = new();

    [Fact]
    public void CompatibleCapabilityReportAllowsPlanning()
    {
        var result = _validator.Validate(SampleManifests.MetadataManifest(), SampleManifests.PortableCapabilities());

        Assert.True(result.IsCompatible);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void UnsupportedRequiredIndexCapabilityBlocksCompatibility()
    {
        var capabilities = SampleManifests.PortableCapabilities() with
        {
            Indexes = IndexCapabilities.All with { SupportsUniqueIndexes = false }
        };

        var result = _validator.Validate(SampleManifests.MetadataManifest(), capabilities);

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == "GW-CAP-007");
    }

    [Fact]
    public void UnsupportedConcurrencyModeBlocksCompatibility()
    {
        var capabilities = SampleManifests.PortableCapabilities() with
        {
            SupportedConcurrencyModes = new HashSet<ConcurrencyKind> { ConcurrencyKind.None }
        };

        var result = _validator.Validate(SampleManifests.MetadataManifest(), capabilities);

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == "GW-CAP-005");
    }

    [Fact]
    public void SupportedFallbackEmitsWarningWithoutChangingManifestIntent()
    {
        var manifest = SampleManifests.MetadataManifest();
        var capabilities = SampleManifests.PortableCapabilities() with
        {
            Warnings = ["Provider will materialize indexes lazily."]
        };

        var result = _validator.Validate(manifest, capabilities);

        Assert.True(result.IsCompatible);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "GW-CAP-002");
        Assert.Equal(ConcurrencyKind.Optimistic, manifest.StorageUnits.Single().Concurrency.Kind);
    }

    [Fact]
    public void MissingSchemaHistorySupportBlocksCompatibility()
    {
        var capabilities = SampleManifests.PortableCapabilities() with { SupportsSchemaHistory = false };

        var result = _validator.Validate(SampleManifests.MetadataManifest(), capabilities);

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == "GW-CAP-001");
    }

    [Fact]
    public void MissingSchemaHistorySupportEmitsSingleDiagnosticWhenRequiredExplicitly()
    {
        var capabilities = SampleManifests.PortableCapabilities() with { SupportsSchemaHistory = false };

        var result = _validator.Validate(SampleManifests.MetadataManifest(), capabilities);

        Assert.False(result.IsCompatible);
        Assert.Equal(1, result.Errors.Count(diagnostic => diagnostic.Code == "GW-CAP-001"));
    }

    [Fact]
    public void UnknownManifestRequiredCapabilityBlocksCompatibility()
    {
        var manifest = SampleManifests.MetadataManifest() with
        {
            RequiredCapabilities = new HashSet<string> { "schema-history", "custom-required-capability" }
        };

        var result = _validator.Validate(manifest, SampleManifests.PortableCapabilities());

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == "GW-CAP-012");
    }

    [Fact]
    public void UnsupportedOptimizedProjectionMaterializationBlocksCompatibility()
    {
        var manifest = SampleManifests.MetadataManifest();
        var optimizedUnit = manifest.StorageUnits.Single() with { Physicalization = PhysicalizationPolicy.Optimized };
        var capabilities = SampleManifests.PortableCapabilities() with
        {
            SupportedMaterializationOperations =
                Enum.GetValues<MaterializationOperationKind>()
                    .Where(operation => operation != MaterializationOperationKind.CreateOptimizedProjection)
                    .ToHashSet()
        };

        var result = _validator.Validate(manifest with { StorageUnits = [optimizedUnit] }, capabilities);

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == "GW-CAP-011");
    }
}
