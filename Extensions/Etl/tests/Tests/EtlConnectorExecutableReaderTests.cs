using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Etl.Core;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed class EtlConnectorExecutableReaderTests
{
    [Fact]
    public void Runtime_feature_resolves_v2_reader_without_design_services_and_reads_compiled_artifact()
    {
        var services = new ServiceCollection();
        CreateFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var artifact = Compile(EtlV2TestPipelines.Create());

        Assert.Null(provider.GetService<IEtlArtifactCompiler>());
        Assert.Null(provider.GetService<IEtlDefinitionStore>());
        var reader = scope.ServiceProvider.GetRequiredService<EtlConnectorExecutableReader>();
        Assert.Equal(artifact.Pipeline.GetRawText(), EtlArtifactV2Json.Write(reader.Read(artifact)).GetRawText());
    }

    [Theory]
    [InlineData("people;drop", "id", "id")]
    [InlineData("people", "full name", "full name")]
    [InlineData("people", "id", "missing")]
    [InlineData("p234567890123456789012345678901234567890123456789012345678901234", "id", "id")]
    [InlineData("people", "c234567890123456789012345678901234567890123456789012345678901234", "c234567890123456789012345678901234567890123456789012345678901234")]
    public void Reader_refuses_a_sql_source_whose_identifiers_or_key_the_compiler_would_refuse(string table, string column, string key)
    {
        using var runtime = CreateRuntime(_ => { });
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = defaults with
        {
            Source = new EtlSqlSource("people-db", table, [new EtlColumn(column, EtlColumnType.Int64, true)], [key]),
            Lookup = null,
            Mapping = [new EtlOutputField("id", EtlFieldOrigin.Source, column)],
            Filters = []
        };

        // A worker re-checks what the compiler enforced, because the dialect quotes these identifiers (spec 1198).
        Assert.Equal("etl.executable.invalid-pipeline", Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(CreateArtifact(pipeline))).Code);
        Assert.NotNull(runtime.Reader.Read(CreateArtifact(pipeline with
        {
            Source = new EtlSqlSource("people-db", "dbo.people", [new EtlColumn("id", EtlColumnType.Int64, true)], ["id"]),
            Mapping = [new EtlOutputField("id", EtlFieldOrigin.Source, "id")]
        })));
    }

    [Theory]
    [InlineData("", "orders", true, false)]
    [InlineData("bad exchange", "orders", false, false)]
    [InlineData("events", "two words", false, false)]
    [InlineData("events", "orders", false, true)]
    [InlineData("e234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456", "orders", false, false)]
    [InlineData("events", "k234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456", false, false)]
    public void Reader_refuses_a_rabbitmq_destination_the_compiler_would_refuse(string exchange, string routingKey, bool valid, bool onErrorRoute)
    {
        using var runtime = CreateRuntime(_ => { });
        var defaults = EtlV2TestPipelines.Create();
        var destination = new EtlRabbitMqDestination("events", exchange, routingKey);
        var pipeline = defaults with
        {
            Routes = onErrorRoute ? new(new EtlJsonlDestination("output"), destination) : new(destination, new EtlJsonlDestination("errors"))
        };

        // The exchange and routing key reach the broker as protocol strings, and only the main route publishes (spec 1200).
        if (valid)
            Assert.NotNull(runtime.Reader.Read(CreateArtifact(pipeline)));
        else
            Assert.Equal("etl.executable.invalid-pipeline", Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(CreateArtifact(pipeline))).Code);
    }

    [Theory]
    [InlineData("people", 10, true)]
    [InlineData("bad queue", 10, false)]
    [InlineData("", 10, false)]
    [InlineData("people", 0, false)]
    [InlineData("people", 100_001, false)]
    public void Reader_refuses_a_rabbitmq_source_the_compiler_would_refuse(string queue, int maxMessages, bool valid)
    {
        using var runtime = CreateRuntime(_ => { });
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = defaults with
        {
            Source = new EtlRabbitMqSource("events-in", queue, [new EtlJsonField("id", "/id", EtlColumnType.Int64, true)], maxMessages),
            Lookup = null,
            Mapping = [new EtlOutputField("id", EtlFieldOrigin.Source, "id")],
            Filters = []
        };

        // The queue reaches the broker as a protocol short string and the maximum bounds a run (spec 1201).
        if (valid)
            Assert.NotNull(runtime.Reader.Read(CreateArtifact(pipeline)));
        else
            Assert.Equal("etl.executable.invalid-pipeline", Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(CreateArtifact(pipeline))).Code);
    }

    [Fact]
    public void Reader_refuses_a_sql_source_with_columns_equal_ignoring_case_or_a_repeated_key()
    {
        using var runtime = CreateRuntime(_ => { });
        var defaults = EtlV2TestPipelines.Create();
        EtlPipelineV2 With(EtlColumn[] columns, string[] key) => defaults with
        {
            Source = new EtlSqlSource("people-db", "people", columns, key),
            Lookup = null,
            Mapping = [new EtlOutputField("id", EtlFieldOrigin.Source, "id")],
            Filters = []
        };

        Assert.Equal("etl.executable.invalid-pipeline", Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(CreateArtifact(
            With([new EtlColumn("id", EtlColumnType.Int64, true), new EtlColumn("ID", EtlColumnType.Int64, true)], ["id"])))).Code);
        Assert.Equal("etl.executable.invalid-pipeline", Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(CreateArtifact(
            With([new EtlColumn("id", EtlColumnType.Int64, true)], ["id", "id"])))).Code);
        // A watermark is a declared, required Int64 column (spec 1199).
        Assert.Equal("etl.executable.invalid-pipeline", Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(CreateArtifact(
            With([new EtlColumn("id", EtlColumnType.Int64, true), new EtlColumn("name", EtlColumnType.String, true)], ["id"]) is var named
                ? named with { Source = ((EtlSqlSource)named.Source) with { Watermark = "name" } } : named))).Code);
        // The key columns are required, so a key value is never null.
        Assert.Equal("etl.executable.invalid-pipeline", Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(CreateArtifact(
            With([new EtlColumn("id", EtlColumnType.Int64, false)], ["id"])))).Code);
        // The key is one to eight columns.
        var nine = Enumerable.Range(0, 9).Select(index => new EtlColumn(index == 0 ? "id" : $"k{index}", EtlColumnType.Int64, true)).ToArray();
        Assert.Equal("etl.executable.invalid-pipeline", Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(CreateArtifact(
            With(nine, nine.Select(column => column.Name).ToArray())))).Code);
        Assert.Equal("etl.executable.invalid-pipeline", Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(CreateArtifact(
            With([new EtlColumn("id", EtlColumnType.Int64, true)], [])))).Code);
    }

    [Fact]
    public void Reader_refuses_http_policy_above_independent_host_ceiling()
    {
        using var runtime = CreateRuntime(feature => feature.HttpLimits = new EtlHttpHostLimits
        {
            MaxPageBytes = 1024,
            MaxContinuationBytes = 1024,
            MaxRetainedInputBytes = 16 * 1024 * 1024,
            MaxRetainedMetadataBytes = 64L * 1024 * 1024
        });

        var exception = Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(Compile(EtlV2TestPipelines.Create())));

        Assert.Equal("etl.executable.limits-exceeded", exception.Code);
    }

    [Fact]
    public void Reader_enforces_positive_host_and_authored_metadata_budgets_independently_of_input_budget()
    {
        Assert.Equal(256L * 1024 * 1024, new EtlHttpHostLimits().MaxRetainedMetadataBytes);

        using var runtime = CreateRuntime(feature => feature.HttpLimits = new EtlHttpHostLimits
        {
            MaxRetainedMetadataBytes = 32L * 1024 * 1024
        });
        var defaults = EtlV2TestPipelines.Create();
        var aboveHost = defaults with
        {
            Http = defaults.Http with { MaxRetainedInputBytes = 2L * 1024 * 1024 * 1024, MaxRetainedMetadataBytes = 64L * 1024 * 1024 }
        };

        var exception = Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(CreateArtifact(aboveHost)));

        Assert.Equal("etl.executable.limits-exceeded", exception.Code);
        var independentBudgets = defaults with
        {
            Http = defaults.Http with { MaxRetainedInputBytes = 16L * 1024 * 1024, MaxRetainedMetadataBytes = 32L * 1024 * 1024 }
        };
        Assert.Equal(32L * 1024 * 1024, runtime.Reader.Read(Compile(independentBudgets)).Http.MaxRetainedMetadataBytes);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Runtime_options_reject_non_positive_host_metadata_budget(long maxRetainedMetadataBytes)
    {
        Assert.Throws<InvalidOperationException>(() => CreateRuntime(feature => feature.HttpLimits = new EtlHttpHostLimits
        {
            MaxRetainedMetadataBytes = maxRetainedMetadataBytes
        }));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Reader_rejects_non_positive_authored_metadata_budget(long maxRetainedMetadataBytes)
    {
        using var runtime = CreateRuntime();
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = defaults with
        {
            Http = defaults.Http with { MaxRetainedMetadataBytes = maxRetainedMetadataBytes }
        };

        var exception = Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(CreateArtifact(pipeline)));

        Assert.Equal("etl.executable.limits-exceeded", exception.Code);
    }

    [Fact]
    public void Reader_accepts_old_v2_http_document_without_rewriting_its_hash_or_bytes()
    {
        var node = JsonNode.Parse(EtlArtifactV2Json.Write(EtlV2TestPipelines.Create()).GetRawText())!;
        node["http"]!.AsObject().Remove("maxRetainedMetadataBytes");
        using var document = JsonDocument.Parse(node.ToJsonString());
        var originalPipeline = document.RootElement.Clone();
        var originalPipelineJson = originalPipeline.GetRawText();
        var originalHash = EtlArtifactJson.Hash(originalPipeline);
        var artifact = new EtlArtifact(2,
            EtlArtifactJson.CreateArtifactId(2, "orders", 1, originalHash), originalHash,
            "orders", 1, originalPipeline);
        using var runtime = CreateRuntime();

        var restored = runtime.Reader.Read(artifact);

        Assert.Equal(64L * 1024 * 1024, restored.Http.MaxRetainedMetadataBytes);
        Assert.Equal(originalHash, artifact.ContentHash);
        Assert.Equal(originalPipelineJson, artifact.Pipeline.GetRawText());
        Assert.False(artifact.Pipeline.GetProperty("http").TryGetProperty("maxRetainedMetadataBytes", out _));
    }

    [Fact]
    public void Host_and_executable_readers_enforce_the_supported_continuation_ceiling()
    {
        Assert.Throws<InvalidOperationException>(() => CreateRuntime(feature => feature.HttpLimits = new EtlHttpHostLimits
        {
            MaxContinuationBytes = EtlHttpPolicy.MaximumSupportedContinuationBytes + 1
        }));

        using var runtime = CreateRuntime(feature => feature.HttpLimits = new EtlHttpHostLimits
        {
            MaxContinuationBytes = EtlHttpPolicy.MaximumSupportedContinuationBytes
        });
        var defaults = EtlV2TestPipelines.Create();
        var boundaryPipeline = defaults with
        {
            Http = defaults.Http with
            {
                MaxContinuationBytes = EtlHttpPolicy.MaximumSupportedContinuationBytes
            }
        };
        Assert.Equal(EtlHttpPolicy.MaximumSupportedContinuationBytes,
            runtime.Reader.Read(Compile(boundaryPipeline)).Http.MaxContinuationBytes);

        var unsupportedPipeline = boundaryPipeline with
        {
            Http = boundaryPipeline.Http with { MaxContinuationBytes = EtlHttpPolicy.MaximumSupportedContinuationBytes + 1 }
        };
        var exception = Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(CreateArtifact(unsupportedPipeline)));
        Assert.Equal("etl.executable.limits-exceeded", exception.Code);
    }

    [Theory]
    [InlineData("single-page", "etl.continuous-pagination-unsupported")]
    [InlineData("zero-interval", "etl.continuous-poll-interval-invalid")]
    [InlineData("long-interval", "etl.continuous-poll-interval-invalid")]
    public void Reader_refuses_unsupported_continuous_artifacts(string variant, string code)
    {
        using var runtime = CreateRuntime();

        var exception = Assert.Throws<EtlExecutableException>(() =>
            runtime.Reader.Read(CreateArtifact(EtlContinuousRunPolicyTests.Variant(variant))));

        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public void Reader_accepts_a_continuous_cursor_artifact()
    {
        using var runtime = CreateRuntime();
        var pipeline = EtlV2TestPipelines.Create() with { Continuous = new EtlContinuousRunPolicy(15) };

        Assert.Equal(15, runtime.Reader.Read(Compile(pipeline)).Continuous!.PollIntervalSeconds);
    }

    [Fact]
    public void Reader_refuses_artifact_batch_limits_above_live_host_capacity()
    {
        using var runtime = CreateRuntime(feature => feature.MaxLiveInputBytes = 1024);

        var exception = Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(Compile(EtlV2TestPipelines.Create())));

        Assert.Equal("etl.executable.limits-exceeded", exception.Code);
    }

    [Fact]
    public void Pipeline_without_lookup_does_not_consume_or_require_lookup_capacity()
    {
        using var runtime = CreateRuntime(feature =>
        {
            feature.MaxLiveLookupBytes = 1;
            feature.MaxLookupRecords = 1;
            feature.MaxLookupCells = 1;
        });

        var pipeline = runtime.Reader.Read(Compile(EtlV2TestPipelines.Create()));

        Assert.Null(pipeline.Lookup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reader_refuses_forged_semantic_mapping_or_filter(bool invalidFilter)
    {
        var pipeline = EtlV2TestPipelines.Create();
        if (invalidFilter)
        {
            pipeline = pipeline with
            {
                Filters = [new EtlFilter("unmapped", EtlComparison.Equal, JsonSerializer.SerializeToElement("Ada"))]
            };
        }
        else
        {
            pipeline = pipeline with
            {
                Mapping = [new EtlOutputField("name", EtlFieldOrigin.Source, "not-in-schema")]
            };
        }

        using var runtime = CreateRuntime();
        var exception = Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(CreateArtifact(pipeline)));

        Assert.Equal("etl.executable.invalid-pipeline", exception.Code);
    }

    [Fact]
    public void Reader_requires_canonical_pipeline_json_even_when_hash_and_identity_match()
    {
        var artifact = Compile(EtlV2TestPipelines.Create());
        using var noncanonicalDocument = JsonDocument.Parse(artifact.Pipeline.GetRawText().Replace(",", ", ", StringComparison.Ordinal));
        var hash = EtlArtifactJson.Hash(noncanonicalDocument.RootElement);
        var noncanonical = new EtlArtifact(2,
            EtlArtifactJson.CreateArtifactId(2, artifact.DefinitionId, artifact.DefinitionVersion, hash),
            hash, artifact.DefinitionId, artifact.DefinitionVersion, noncanonicalDocument.RootElement);
        using var runtime = CreateRuntime();

        var exception = Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(noncanonical));

        Assert.Equal("etl.executable.noncanonical-pipeline", exception.Code);
    }

    [Fact]
    public void Reader_accepts_a_jsonl_source_and_refuses_a_field_count_over_its_limit()
    {
        var pipeline = EtlJsonlInputTests.JsonlPipeline();
        using var runtime = CreateRuntime();

        Assert.IsType<EtlJsonlSource>(runtime.Reader.Read(Compile(pipeline)).Source);
        var crowded = pipeline with { Limits = pipeline.Limits with { MaxFields = 1 } };
        Assert.Equal("etl.executable.limits-exceeded",
            Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(CreateArtifact(crowded))).Code);
    }

    [Fact]
    public void Reader_accepts_csv_destinations()
    {
        using var runtime = CreateRuntime();
        var pipeline = EtlV2TestPipelines.Create() with { Routes = new(new EtlCsvDestination("output"), new EtlCsvDestination("errors")) };

        Assert.IsType<EtlCsvDestination>(runtime.Reader.Read(Compile(pipeline)).Routes.Errors);
    }

    [Fact]
    public void Reader_verifies_content_hash_and_artifact_identity()
    {
        var artifact = Compile(EtlV2TestPipelines.Create());
        using var runtime = CreateRuntime();
        var wrongHashValue = (artifact.ContentHash[0] == '0' ? '1' : '0') + artifact.ContentHash[1..];
        var wrongHash = new EtlArtifact(artifact.FormatVersion, artifact.ArtifactId, wrongHashValue,
            artifact.DefinitionId, artifact.DefinitionVersion, artifact.Pipeline);
        var wrongId = new EtlArtifact(artifact.FormatVersion, "wrong-artifact-id", artifact.ContentHash,
            artifact.DefinitionId, artifact.DefinitionVersion, artifact.Pipeline);

        Assert.Equal("etl.executable.provenance-invalid", Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(wrongHash)).Code);
        Assert.Equal("etl.executable.provenance-invalid", Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(wrongId)).Code);
    }

    [Fact]
    public void Reader_checks_raw_pipeline_size_before_typed_deserialization()
    {
        using var document = JsonDocument.Parse("{\"padding\":\"" + new string('x', EtlArtifact.MaximumByteLength) + "\"}");
        var oversized = new EtlArtifact(2, "etl-v2-placeholder", new string('0', 64), "orders", 1, document.RootElement);
        using var runtime = CreateRuntime();

        var exception = Assert.Throws<EtlExecutableException>(() => runtime.Reader.Read(oversized));

        Assert.Equal("etl.executable.too-large", exception.Code);
    }

    private static EtlArtifact Compile(EtlPipelineV2 pipeline)
    {
        var result = new EtlArtifactCompiler().Compile("orders", 1, pipeline);
        Assert.Empty(result.Diagnostics);
        return Assert.IsType<EtlArtifact>(result.Artifact);
    }

    // These tests deliberately rebuild provenance around malformed-but-canonical pipeline documents to
    // exercise the runtime's semantic checks independently of the design compiler.
    private static EtlArtifact CreateArtifact(EtlPipelineV2 pipeline)
    {
        var document = EtlArtifactV2Json.Write(pipeline);
        var hash = EtlArtifactJson.Hash(document);
        return new EtlArtifact(2, EtlArtifactJson.CreateArtifactId(2, "orders", 1, hash), hash, "orders", 1, document);
    }

    private static EtlRuntimeFeature CreateFeature(Action<EtlRuntimeFeature>? configure = null)
    {
        var feature = new EtlRuntimeFeature
        {
            SnapshotRoot = Path.Combine(Path.GetTempPath(), "elsa-etl-connector-executable-reader")
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
