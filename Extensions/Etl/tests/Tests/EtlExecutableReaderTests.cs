using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Design;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed class EtlExecutableReaderTests
{
    [Fact]
    public void Reader_accepts_an_artifact_created_by_the_design_compiler()
    {
        var compiler = CreateCompiler();
        var artifact = compiler.Compile("orders", 1, EtlTestPipelines.Create()).Artifact!;
        using var runtime = CreateRuntime();

        var pipeline = runtime.Reader.Read(artifact);

        Assert.Equal(EtlArtifactJson.Write(EtlTestPipelines.Create()).GetRawText(), EtlArtifactJson.Write(pipeline).GetRawText());
        Assert.Equal(EtlArtifactJson.Hash(artifact.Pipeline), artifact.ContentHash);
        Assert.Equal(EtlArtifactJson.CreateArtifactId(artifact.FormatVersion, artifact.DefinitionId, artifact.DefinitionVersion, artifact.ContentHash), artifact.ArtifactId);
    }

    [Fact]
    public void Reader_rejects_a_noncanonical_pipeline_even_when_its_content_hash_matches()
    {
        var artifact = CreateArtifact(EtlTestPipelines.Create());
        using var document = JsonDocument.Parse(artifact.Pipeline.GetRawText().Replace(",", ", ", StringComparison.Ordinal));
        var noncanonical = new EtlArtifact(artifact.FormatVersion, artifact.ArtifactId, artifact.ContentHash,
            artifact.DefinitionId, artifact.DefinitionVersion, document.RootElement);
        using var runtime = CreateRuntime();

        var exception = Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(noncanonical));

        Assert.Equal("etl.executable.noncanonical-pipeline", exception.Code);
    }

    [Fact]
    public void Reader_checks_raw_pipeline_size_before_typed_deserialization()
    {
        using var document = JsonDocument.Parse("{\"unknown\":\"" + new string('x', EtlArtifact.MaximumByteLength) + "\"}");
        var artifact = new EtlArtifact(1, "etl-v1-placeholder", new string('0', 64), "orders", 1, document.RootElement);
        using var runtime = CreateRuntime();

        var exception = Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(artifact));

        Assert.Equal("etl.executable.too-large", exception.Code);
    }

    [Fact]
    public void Reader_enforces_the_complete_artifact_envelope_limit()
    {
        var pipeline = EtlTestPipelines.Create();
        var initial = CreateArtifact(pipeline);
        var rawSize = Encoding.UTF8.GetByteCount(initial.Pipeline.GetRawText());
        var envelopeSize = JsonSerializer.SerializeToUtf8Bytes(initial).Length;
        Assert.True(envelopeSize > rawSize);

        var limit = rawSize + (envelopeSize - rawSize) / 2;
        var limitedPipeline = pipeline with { Limits = pipeline.Limits with { MaxArtifactBytes = limit } };
        var limitedArtifact = CreateArtifact(limitedPipeline);
        var limitedRawSize = Encoding.UTF8.GetByteCount(limitedArtifact.Pipeline.GetRawText());
        var limitedEnvelopeSize = JsonSerializer.SerializeToUtf8Bytes(limitedArtifact).Length;
        Assert.True(limitedRawSize <= limit);
        Assert.True(limitedEnvelopeSize > limit);
        using var runtime = CreateRuntime();

        var exception = Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(limitedArtifact));

        Assert.Equal("etl.executable.too-large", exception.Code);
    }

    [Fact]
    public void Reader_rejects_pipeline_limits_that_exceed_host_capacity()
    {
        var limits = new EtlLimits { MaxBatchRecords = 20_001, MaxOutstandingRecords = 20_001 };
        var artifact = CreateArtifact(EtlTestPipelines.Create() with { Limits = limits });
        using var runtime = CreateRuntime();

        var exception = Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(artifact));

        Assert.Equal("etl.executable.limits-exceeded", exception.Code);
    }

    [Fact]
    public void Reader_rejects_lookup_cardinality_above_the_host_lookup_ceiling()
    {
        var limits = new EtlLimits { MaxLookupRecords = 100_001 };
        var artifact = CreateArtifact(EtlTestPipelines.Create() with { Limits = limits });
        using var runtime = CreateRuntime();

        var exception = Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(artifact));

        Assert.Equal("etl.executable.limits-exceeded", exception.Code);
    }

    [Fact]
    public void Reader_caps_lookup_cells_before_materializing_a_wide_sparse_lookup()
    {
        var pipeline = EtlTestPipelines.Create();
        pipeline = pipeline with
        {
            LookupSchema = [pipeline.LookupSchema[0], .. Enumerable.Range(1, 255)
                .Select(index => new EtlColumn($"optional{index}", EtlColumnType.String, false))],
            Limits = pipeline.Limits with { MaxFields = 256, MaxLookupRecords = 100_000 }
        };
        var artifact = Assert.IsType<EtlArtifact>(CreateCompiler().Compile("wide", 1, pipeline).Artifact);
        using var runtime = CreateRuntime();

        var exception = Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(artifact));

        Assert.Equal("etl.executable.limits-exceeded", exception.Code);
    }

    [Fact]
    public void Reader_rejects_a_forged_unknown_mapping_field()
    {
        var pipeline = EtlTestPipelines.Create() with
        {
            Mapping = [new EtlOutputField("name", EtlFieldOrigin.Source, "not-in-schema")]
        };
        using var runtime = CreateRuntime();

        var exception = Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(CreateArtifact(pipeline)));

        Assert.Equal("etl.executable.invalid-pipeline", exception.Code);
    }

    [Fact]
    public void Reader_rejects_a_forged_filter_literal_with_the_wrong_type()
    {
        using var literal = JsonDocument.Parse("7");
        var pipeline = EtlTestPipelines.Create() with
        {
            Filters = [new EtlFilter("name", EtlComparison.Equal, literal.RootElement)]
        };
        using var runtime = CreateRuntime();

        var exception = Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(CreateArtifact(pipeline)));

        Assert.Equal("etl.executable.invalid-pipeline", exception.Code);
    }

    [Fact]
    public void Runtime_feature_accepts_null_tenant_scope_without_registering_design_services()
    {
        var feature = CreateFeature();
        var collection = new ServiceCollection();

        feature.ConfigureServices(collection);
        using var provider = collection.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Null(provider.GetService<IEtlArtifactCompiler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<EtlExecutableReader>());
        Assert.Equal("partition-a", provider.GetRequiredService<EtlRuntimeOptions>().FileBindings[0].Scope.PersistencePartition);
        Assert.Null(provider.GetRequiredService<EtlRuntimeOptions>().FileBindings[0].Scope.TenantId);
    }

    [Fact]
    public void Runtime_feature_requires_an_absolute_snapshot_root()
    {
        var collection = new ServiceCollection();
        var feature = CreateFeature();
        feature.SnapshotRoot = "relative/snapshots";

        Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(collection));
    }

    [Fact]
    public void Runtime_feature_requires_an_explicit_persistence_partition()
    {
        var collection = new ServiceCollection();
        var feature = CreateFeature();
        feature.FileBindings[0].PersistencePartition = string.Empty;

        Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(collection));
    }

    private static IEtlArtifactCompiler CreateCompiler()
    {
        var services = new ServiceCollection();
        new EtlDesignFeature().ConfigureServices(services);
        return services.BuildServiceProvider().GetRequiredService<IEtlArtifactCompiler>();
    }

    private static RuntimeProvider CreateRuntime()
    {
        var services = new ServiceCollection();
        CreateFeature().ConfigureServices(services);
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return new RuntimeProvider(provider, scope, scope.ServiceProvider.GetRequiredService<EtlExecutableReader>());
    }

    private static EtlRuntimeFeature CreateFeature()
    {
        var root = Path.Combine(Path.GetTempPath(), "elsa-etl-executable-reader");
        return new EtlRuntimeFeature
        {
            SnapshotRoot = root,
            FileBindings =
            [
                new() { TenantId = null, PersistencePartition = "partition-a", Alias = "source", Role = "Source", Revision = "source-r1", Path = Path.Combine(root, "source.csv"), IsCurrent = true, ExpectedSha256 = new string('a', 64), ImmutableInput = true },
                new() { TenantId = null, PersistencePartition = "partition-a", Alias = "lookup", Role = "Lookup", Revision = "lookup-r1", Path = Path.Combine(root, "lookup.csv"), IsCurrent = true, ExpectedSha256 = new string('b', 64), ImmutableInput = true },
                new() { TenantId = null, PersistencePartition = "partition-a", Alias = "output", Role = "MainOutput", Revision = "output-r1", Path = Path.Combine(root, "output"), IsCurrent = true, AtomicPublication = true },
                new() { TenantId = null, PersistencePartition = "partition-a", Alias = "errors", Role = "ErrorOutput", Revision = "errors-r1", Path = Path.Combine(root, "errors"), IsCurrent = true, AtomicPublication = true }
            ]
        };
    }

    private static EtlArtifact CreateArtifact(EtlPipeline pipeline)
    {
        var document = EtlArtifactJson.Write(pipeline);
        var hash = EtlArtifactJson.Hash(document);
        return new EtlArtifact(EtlArtifact.CurrentFormatVersion, EtlArtifactJson.CreateArtifactId(EtlArtifact.CurrentFormatVersion, "orders", 1, hash), hash, "orders", 1, document);
    }


    private sealed class RuntimeProvider(ServiceProvider provider, IServiceScope scope, EtlExecutableReader reader) : IDisposable
    {
        public EtlExecutableReader Reader { get; } = reader;

        public void Dispose()
        {
            scope.Dispose();
            provider.Dispose();
        }
    }
}
