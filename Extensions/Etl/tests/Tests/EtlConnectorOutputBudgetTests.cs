using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed class EtlConnectorOutputBudgetTests
{
    private const long OneGiB = 1024L * 1024 * 1024;
    private const string LegacyDefaultPipelineJson = """{"source":{"kind":"httpJson","binding":"source","recordsPointer":"/records","fields":[{"name":"id","pointer":"/id","type":"int64","required":true},{"name":"name","pointer":"/name","type":"string","required":true}],"pagination":{"kind":"token","continuationPointer":"/next","queryParameter":"cursor"}},"lookup":null,"routes":{"main":{"kind":"http","binding":"output"},"errors":{"kind":"http","binding":"errors"}},"mapping":[{"name":"name","origin":"source","field":"name"}],"filters":[],"limits":{"filePartitionCount":2,"maxPartitions":32,"maxRecordBytes":65536,"maxFields":256,"maxBatchRecords":1000,"maxBatchBytes":4194304,"maxOutstandingRecords":10000,"maxOutstandingBytes":33554432,"maxLookupRecords":100000,"maxLookupBytes":16777216,"maxOutputBatchBytes":8388608,"maxSnapshotBytes":1073741824,"maxArtifactBytes":262144},"http":{"maxPageBytes":8388608,"maxPageRecords":10000,"maxJsonDepth":32,"maxContinuationBytes":8192,"maxPagesPerPartition":10000,"maxRetainedInputBytes":1073741824,"maxAttempts":5,"deadlineSeconds":900,"requestTimeoutSeconds":30,"initialDelaySeconds":1,"maxDelaySeconds":60}}""";

    [Fact]
    public void Legacy_default_pipeline_json_retains_its_bytes_and_artifact_hash()
    {
        using var document = JsonDocument.Parse(LegacyDefaultPipelineJson);
        var originalPipeline = document.RootElement.Clone();
        var originalHash = EtlArtifactJson.Hash(originalPipeline);
        using var runtime = CreateRuntime();
        var artifact = CreateArtifact(originalPipeline, originalHash);

        var restored = runtime.Reader.Read(artifact);

        Assert.Equal(OneGiB, restored.Limits.MaxRetainedOutputBytes);
        Assert.False(originalPipeline.GetProperty("limits").TryGetProperty("maxRetainedOutputBytes", out _));
        Assert.Equal(LegacyDefaultPipelineJson, EtlArtifactV2Json.Write(restored).GetRawText());
        Assert.Equal(originalHash, EtlArtifactJson.Hash(EtlArtifactV2Json.Write(restored)));
    }

    [Fact]
    public void Compiler_accepts_and_serializes_explicit_retained_output_budget()
    {
        const long budget = 2L * OneGiB;
        var pipeline = EtlV2TestPipelines.Create() with
        {
            Limits = new EtlConnectorLimits { MaxRetainedOutputBytes = budget }
        };

        var result = Compile(pipeline);

        Assert.Empty(result.Diagnostics);
        var artifact = Assert.IsType<EtlArtifact>(result.Artifact);
        Assert.Equal(budget, artifact.Pipeline.GetProperty("limits").GetProperty("maxRetainedOutputBytes").GetInt64());
        Assert.Equal(budget, EtlArtifactV2Json.Read(artifact.Pipeline).Limits.MaxRetainedOutputBytes);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Compiler_and_store_codec_reject_nonpositive_retained_output_budget(long budget)
    {
        var pipeline = EtlV2TestPipelines.Create() with
        {
            Limits = new EtlConnectorLimits { MaxRetainedOutputBytes = budget }
        };

        var result = Compile(pipeline);
        Assert.Null(result.Artifact);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "limits.invalid");
        Assert.Throws<ArgumentException>(() => EtlConnectorRecordCodec.ValidateBindings(pipeline, Bindings(pipeline), maximumPartitions: 32));
    }

    [Fact]
    public void Store_codec_rejects_a_canonical_null_document()
    {
        Assert.Throws<InvalidDataException>(() => EtlConnectorRecordCodec.Read<EtlConnectorBatchOutput>("null"));
    }

    [Fact]
    public void Runtime_options_apply_host_ceiling_and_reader_enforces_authored_output_budget()
    {
        const long hostBudget = 2L * OneGiB;
        using var runtime = CreateRuntime(feature => feature.MaxRetainedOutputBytes = hostBudget);
        Assert.Equal(hostBudget, EtlRuntimeOptions.Create(CreateFeature(feature => feature.MaxRetainedOutputBytes = hostBudget)).MaxRetainedOutputBytes);

        var boundary = EtlV2TestPipelines.Create() with
        {
            Limits = new EtlConnectorLimits { MaxRetainedOutputBytes = hostBudget }
        };
        Assert.Equal(hostBudget, runtime.Reader.Read(CompileArtifact(boundary)).Limits.MaxRetainedOutputBytes);

        var overHost = boundary with
        {
            Limits = boundary.Limits with { MaxRetainedOutputBytes = hostBudget + 1 }
        };
        Assert.Equal("etl.executable.limits-exceeded",
            Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(CreateArtifact(overHost))).Code);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Host_options_and_runtime_reader_reject_nonpositive_output_budgets(long budget)
    {
        Assert.Throws<InvalidOperationException>(() => EtlRuntimeOptions.Create(CreateFeature(feature => feature.MaxRetainedOutputBytes = budget)));

        using var runtime = CreateRuntime();
        var invalidPipeline = EtlV2TestPipelines.Create() with
        {
            Limits = new EtlConnectorLimits { MaxRetainedOutputBytes = budget }
        };
        Assert.Equal("etl.executable.limits-exceeded",
            Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(CreateArtifact(invalidPipeline))).Code);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Store_codec_rejects_nonpositive_output_batch_budget(long budget)
    {
        var pipeline = EtlV2TestPipelines.Create() with
        {
            Limits = new EtlConnectorLimits { MaxOutputBatchBytes = budget }
        };
        Assert.Throws<ArgumentException>(() => EtlConnectorRecordCodec.ValidateBindings(pipeline, Bindings(pipeline), maximumPartitions: 32));
    }

    private static EtlCompilationResult Compile(EtlPipelineV2 pipeline) =>
        new EtlArtifactCompiler().Compile("orders", 1, pipeline);

    private static EtlArtifact CompileArtifact(EtlPipelineV2 pipeline)
    {
        var result = Compile(pipeline);
        Assert.Empty(result.Diagnostics);
        return Assert.IsType<EtlArtifact>(result.Artifact);
    }

    private static EtlArtifact CreateArtifact(EtlPipelineV2 pipeline)
    {
        var document = EtlArtifactV2Json.Write(pipeline);
        return CreateArtifact(document, EtlArtifactJson.Hash(document));
    }

    private static EtlArtifact CreateArtifact(JsonElement document, string contentHash) =>
        new(EtlArtifactV2Json.FormatVersion,
            EtlArtifactJson.CreateArtifactId(EtlArtifactV2Json.FormatVersion, "orders", 1, contentHash),
            contentHash, "orders", 1, document);

    private static EtlConnectorRunBindings Bindings(EtlPipelineV2 pipeline) => new(
        Binding(EtlConnectorKind.Http, ((EtlHttpJsonSource)pipeline.Source).Binding),
        null,
        Binding(EtlConnectorKind.Http, ((EtlHttpDestination)pipeline.Routes.Main).Binding),
        Binding(EtlConnectorKind.Http, ((EtlHttpDestination)pipeline.Routes.Errors).Binding),
        ["shard"]);

    private static EtlConnectorBindingRevision Binding(EtlConnectorKind kind, string alias) =>
        new(kind, new EtlBindingRevision(alias, "revision", "handle", null));

    private static EtlRuntimeFeature CreateFeature(Action<EtlRuntimeFeature>? configure = null)
    {
        var feature = new EtlRuntimeFeature
        {
            SnapshotRoot = Path.Combine(Path.GetTempPath(), "elsa-etl-connector-output-budget")
        };
        configure?.Invoke(feature);
        return feature;
    }

    private static RuntimeReader CreateRuntime(Action<EtlRuntimeFeature>? configure = null)
    {
        var services = new ServiceCollection();
        CreateFeature(configure).ConfigureServices(services);
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return new RuntimeReader(provider, scope, scope.ServiceProvider.GetRequiredService<EtlConnectorExecutableReader>());
    }

    private sealed class RuntimeReader(ServiceProvider provider, IServiceScope scope, EtlConnectorExecutableReader reader) : IDisposable
    {
        public EtlConnectorExecutableReader Reader { get; } = reader;

        public void Dispose()
        {
            scope.Dispose();
            provider.Dispose();
        }
    }
}
