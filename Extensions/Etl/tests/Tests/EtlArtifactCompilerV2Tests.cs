using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed class EtlArtifactCompilerV2Tests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Compile_snapshots_each_source_and_destination_combination(bool csvSource, bool fileDestination)
    {
        var pipeline = EtlV2TestPipelines.Create();
        if (csvSource)
            pipeline = pipeline with { Source = new EtlCsvSource("source", [new("name", EtlColumnType.String, true)]) };
        if (fileDestination)
            pipeline = pipeline with { Routes = new(new EtlJsonlDestination("output"), new EtlJsonlDestination("errors")) };
        var result = Compile(pipeline);
        Assert.Empty(result.Diagnostics);
        var artifact = Assert.IsType<EtlArtifact>(result.Artifact);
        Assert.Equal(2, artifact.FormatVersion);
        Assert.Equal(EtlArtifactJson.Hash(artifact.Pipeline), artifact.ContentHash);
        Assert.Equal(EtlArtifactJson.CreateArtifactId(2, "orders", 1, artifact.ContentHash), artifact.ArtifactId);
        Assert.Equal(artifact.Pipeline.GetRawText(), EtlArtifactV2Json.Write(EtlArtifactV2Json.Read(artifact.Pipeline)).GetRawText());
    }

    [Fact]
    public void Main_and_error_routes_can_use_different_delivery_kinds()
    {
        var pipeline = EtlV2TestPipelines.Create() with { Routes = new(new EtlHttpDestination("output"), new EtlJsonlDestination("errors")) };
        var artifact = Assert.IsType<EtlArtifact>(Compile(pipeline).Artifact);
        var restored = EtlArtifactV2Json.Read(artifact.Pipeline);
        Assert.IsType<EtlHttpDestination>(restored.Routes.Main);
        Assert.IsType<EtlJsonlDestination>(restored.Routes.Errors);
    }

    [Fact]
    public void Optional_lookup_can_supply_a_typed_mapping_and_filter()
    {
        var pipeline = EtlV2TestPipelines.Create() with
        {
            Lookup = new("lookup", [new("key", EtlColumnType.Int64, true), new("category", EtlColumnType.String, true)], "id", "key"),
            Mapping = [new("category", EtlFieldOrigin.Lookup, "category")],
            Filters = [new("category", EtlComparison.Equal, JsonSerializer.SerializeToElement("active"))]
        };
        Assert.Empty(Compile(pipeline).Diagnostics);
        var mismatch = pipeline with { Lookup = pipeline.Lookup with { Schema = [new("key", EtlColumnType.String, true)] } };
        AssertRejected(mismatch, "lookup.key.type-mismatch");
        AssertRejected(pipeline with { Lookup = null }, "mapping.field.missing");
    }

    [Theory]
    [InlineData("../orders", 1, "definition.id.invalid")]
    [InlineData("", 1, "definition.id.required")]
    [InlineData("orders", 0, "definition.version.invalid")]
    public void Definition_identity_is_validated(string id, int version, string code) =>
        AssertDiagnostic(new EtlArtifactCompiler().Compile(id, version, EtlV2TestPipelines.Create()), code);

    [Theory]
    [InlineData("records")]
    [InlineData("/records/~")]
    [InlineData("/records/~2")]
    [InlineData("#/records")]
    public void Invalid_json_pointer_is_rejected_without_echoing_it(string pointer)
    {
        var pipeline = EtlV2TestPipelines.Create();
        var source = (EtlHttpJsonSource)pipeline.Source;
        var result = Compile(pipeline with { Source = source with { RecordsPointer = pointer } });
        AssertDiagnostic(result, "json.pointer.invalid");
        Assert.DoesNotContain(result.Diagnostics, item => item.Message.Contains(pointer, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/records/~0/~1")]
    [InlineData("/a%20b")]
    public void Valid_pointer_syntax_is_accepted(string pointer)
    {
        var pipeline = EtlV2TestPipelines.Create();
        Assert.Empty(Compile(pipeline with { Source = ((EtlHttpJsonSource)pipeline.Source) with { RecordsPointer = pointer } }).Diagnostics);
    }

    [Theory]
    [InlineData("cursor")]
    [InlineData("page[token]")]
    [InlineData("next token")]
    public void Token_parameter_names_are_literals_for_future_URI_encoding(string parameter)
    {
        var p = EtlV2TestPipelines.Create();
        var source = (EtlHttpJsonSource)p.Source;
        Assert.Empty(Compile(p with { Source = source with { Pagination = new EtlTokenPagination("/next", parameter) } }).Diagnostics);
    }

    [Fact]
    public void Invalid_variants_aliases_and_pagination_are_refused()
    {
        var p = EtlV2TestPipelines.Create();
        var source = (EtlHttpJsonSource)p.Source;
        AssertRejected(p with { Source = null! }, "source.required");
        AssertRejected(p with { Source = source with { Binding = "https://private.example/path" } }, "binding.alias.invalid");
        AssertRejected(p with { Source = source with { Fields = [new("name", "/~3", EtlColumnType.String, true)] } }, "json.pointer.invalid");
        AssertRejected(p with { Source = source with { Fields = [new("name", "/name", (EtlColumnType)999, true)] } }, "schema.field.type.invalid");
        AssertRejected(p with { Source = source with { Pagination = new EtlTokenPagination("/next", "cursor\r") } }, "pagination.parameter.invalid");
        AssertRejected(p with { Source = source with { Pagination = null! } }, "pagination.required");
        AssertRejected(p with { Routes = new(null!, new EtlHttpDestination("errors")) }, "destination.required");
    }

    [Fact]
    public void Finite_limits_and_enclosing_budgets_are_validated()
    {
        var p = EtlV2TestPipelines.Create();
        AssertRejected(p with { Limits = p.Limits with { MaxBatchRecords = 0 } }, "limits.invalid");
        AssertRejected(p with { Limits = p.Limits with { FilePartitionCount = 33 } }, "limits.invalid");
        AssertRejected(p with { Limits = p.Limits with { MaxOutstandingBytes = 1 } }, "limits.invalid");
        AssertRejected(p with { Limits = p.Limits with { MaxArtifactBytes = EtlArtifact.MaximumByteLength + 1 } }, "limits.artifact-too-large");
        AssertRejected(p with { Http = p.Http with { MaxAttempts = 0 } }, "http.limits.invalid");
        AssertRejected(p with { Http = p.Http with { MaxJsonDepth = 65 } }, "http.limits.invalid");
        AssertRejected(p with { Http = p.Http with { MaxRetainedInputBytes = 1 } }, "http.limits.invalid");
        AssertRejected(p with { Http = p.Http with { RequestTimeoutSeconds = p.Http.DeadlineSeconds + 1 } }, "http.limits.invalid");
    }

    [Fact]
    public void Continuation_limit_obeys_the_persisted_metadata_ceiling()
    {
        var pipeline = EtlV2TestPipelines.Create();

        AssertRejected(pipeline with
        {
            Http = pipeline.Http with { MaxContinuationBytes = EtlHttpPolicy.MaximumSupportedContinuationBytes + 1 }
        }, "http.limits.invalid");
        Assert.Empty(Compile(pipeline with
        {
            Http = pipeline.Http with { MaxContinuationBytes = EtlHttpPolicy.MaximumSupportedContinuationBytes }
        }).Diagnostics);
    }

    [Fact]
    public void Oversized_input_and_mapping_are_rejected_with_bounded_diagnostics()
    {
        var p = EtlV2TestPipelines.Create();
        AssertRejected(p with { Mapping = [new(new string('x', EtlArtifact.MaximumByteLength), EtlFieldOrigin.Source, "name")] }, "artifact.too-large");
        var result = Compile(p with { Mapping = Enumerable.Repeat(new EtlOutputField("a", EtlFieldOrigin.Lookup, "absent"), 100).ToArray() });
        Assert.Null(result.Artifact);
        Assert.InRange(result.Diagnostics.Count, 1, 32);
    }

    [Fact]
    public void Compilation_snapshots_mutable_collections_and_disambiguates_provenance()
    {
        var mapping = new List<EtlOutputField> { new("name", EtlFieldOrigin.Source, "name") };
        var pipeline = EtlV2TestPipelines.Create() with { Mapping = mapping };
        var compiler = new EtlArtifactCompiler();
        var a = Assert.IsType<EtlArtifact>(compiler.Compile("orders", 1, pipeline).Artifact);
        var b = Assert.IsType<EtlArtifact>(compiler.Compile("orders", 2, pipeline).Artifact);
        Assert.Equal(a.ContentHash, b.ContentHash);
        Assert.NotEqual(a.ArtifactId, b.ArtifactId);
        mapping.Clear();
        Assert.Single(EtlArtifactV2Json.Read(a.Pipeline).Mapping);
    }

    [Fact]
    public void Built_in_feature_registers_both_capabilities_on_one_scoped_compiler()
    {
        var services = new ServiceCollection();
        new EtlDesignFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Same(scope.ServiceProvider.GetRequiredService<IEtlArtifactCompiler>(), scope.ServiceProvider.GetRequiredService<IEtlArtifactCompilerV2>());
    }

    private static EtlCompilationResult Compile(EtlPipelineV2 pipeline) => new EtlArtifactCompiler().Compile("orders", 1, pipeline);
    private static void AssertRejected(EtlPipelineV2 pipeline, string code) => AssertDiagnostic(Compile(pipeline), code);
    private static void AssertDiagnostic(EtlCompilationResult result, string code)
    {
        Assert.Null(result.Artifact);
        Assert.Contains(result.Diagnostics, item => item.Code == code);
    }
}
