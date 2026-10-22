using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Design;

namespace Elsa.Etl.Tests;

public sealed class EtlArtifactCompilerTests
{
    [Fact]
    public void Compile_returns_a_versioned_artifact_for_a_valid_finite_pipeline()
    {
        var result = Compile(CreatePipeline());

        Assert.Empty(result.Diagnostics);
        var artifact = Assert.IsType<EtlArtifact>(result.Artifact);
        Assert.Equal(EtlArtifact.CurrentFormatVersion, artifact.FormatVersion);
        Assert.Equal("orders", artifact.DefinitionId);
        Assert.Equal(4, artifact.DefinitionVersion);
        Assert.Equal(64, artifact.ContentHash.Length);
        Assert.Equal(artifact.ContentHash.ToLowerInvariant(), artifact.ContentHash);
        Assert.Equal(CreatePipeline().Mapping, EtlArtifactJson.Read(artifact.Pipeline).Mapping);
    }

    [Fact]
    public void Input_collection_mutations_after_compile_do_not_change_the_artifact_snapshot()
    {
        var sourceSchema = new List<EtlColumn> { new("id", EtlColumnType.Int64, Required: true) };
        var mapping = new List<EtlOutputField> { new("id", EtlFieldOrigin.Source, "id") };
        var pipeline = CreatePipeline(sourceSchema: sourceSchema, mapping: mapping);

        var artifact = Assert.IsType<EtlArtifact>(Compile(pipeline).Artifact);
        sourceSchema.Clear();
        mapping[0] = new EtlOutputField("changed", EtlFieldOrigin.Source, "id");

        var snapshot = EtlArtifactJson.Read(artifact.Pipeline);
        Assert.Equal("id", Assert.Single(snapshot.SourceSchema).Name);
        Assert.Equal("id", Assert.Single(snapshot.Mapping).Name);
    }

    [Fact]
    public void Content_hash_tracks_pipeline_behavior_but_artifact_id_disambiguates_provenance()
    {
        var pipeline = CreatePipeline();
        var first = Compile("orders", 4, pipeline);
        var samePipelineOtherDefinition = Compile("other-orders", 4, pipeline);
        var samePipelineOtherVersion = Compile("orders", 5, pipeline);
        var changedPipeline = Compile("orders", 4, CreatePipeline(mapping: [new("renamed", EtlFieldOrigin.Source, "id")]));

        var artifact = Assert.IsType<EtlArtifact>(first.Artifact);
        var otherDefinition = Assert.IsType<EtlArtifact>(samePipelineOtherDefinition.Artifact);
        var otherVersion = Assert.IsType<EtlArtifact>(samePipelineOtherVersion.Artifact);
        var changed = Assert.IsType<EtlArtifact>(changedPipeline.Artifact);

        Assert.Equal(artifact.ContentHash, otherDefinition.ContentHash);
        Assert.Equal(artifact.ContentHash, otherVersion.ContentHash);
        Assert.NotEqual(artifact.ArtifactId, otherDefinition.ArtifactId);
        Assert.NotEqual(artifact.ArtifactId, otherVersion.ArtifactId);
        Assert.Equal(artifact.ArtifactId, Assert.IsType<EtlArtifact>(Compile("orders", 4, pipeline).Artifact).ArtifactId);
        Assert.NotEqual(artifact.ContentHash, changed.ContentHash);
        Assert.NotEqual(artifact.ArtifactId, changed.ArtifactId);
    }

    [Theory]
    [InlineData("", 1, "definition.id.required")]
    [InlineData("../private", 1, "definition.id.invalid")]
    [InlineData("résumé", 1, "definition.id.invalid")]
    [InlineData("orders", 0, "definition.version.invalid")]
    [InlineData("orders", -1, "definition.version.invalid")]
    public void Compile_rejects_missing_definition_identity(string definitionId, int version, string code)
    {
        var result = Compile(definitionId, version, CreatePipeline());

        Assert.Null(result.Artifact);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData("../private.csv")]
    [InlineData("C:\\data\\input.csv")]
    [InlineData("résumé")]
    [InlineData("bad alias")]
    public void Compile_rejects_non_alias_binding_values_without_echoing_them(string alias)
    {
        var result = Compile(CreatePipeline(bindings: new EtlFileBindings(alias, "lookup", "output", "errors")));

        Assert.Null(result.Artifact);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "binding.alias.invalid");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Message.Contains(alias, StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Path.Contains(alias, StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_rejects_aliases_longer_than_128_ascii_characters()
    {
        var alias = new string('a', 129);
        var result = Compile(CreatePipeline(bindings: new EtlFileBindings(alias, "lookup", "output", "errors")));

        Assert.Null(result.Artifact);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "binding.alias.invalid");

        var maximumLengthAlias = new string('a', 128);
        Assert.NotNull(Compile(CreatePipeline(bindings: new EtlFileBindings(maximumLengthAlias, "lookup", "output", "errors"))).Artifact);
    }

    [Fact]
    public void Compile_rejects_definition_identifiers_longer_than_256_characters()
    {
        var result = Compile(new string('a', 257), 1, CreatePipeline());

        Assert.Null(result.Artifact);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "definition.id.invalid");
    }

    [Fact]
    public void Compile_validates_unique_ordinal_schema_names_required_lookup_keys_and_matching_types()
    {
        var duplicateSchema = CreatePipeline(sourceSchema:
        [
            new EtlColumn("id", EtlColumnType.Int64, Required: true),
            new EtlColumn("id", EtlColumnType.String, Required: true)
        ]);
        var nonRequiredKey = CreatePipeline(lookupSchema: [new EtlColumn("key", EtlColumnType.Int64, Required: false)]);
        var typeMismatch = CreatePipeline(lookupSchema: [new EtlColumn("key", EtlColumnType.String, Required: true)]);

        AssertInvalid(duplicateSchema, "schema.field.duplicate");
        AssertInvalid(nonRequiredKey, "lookup.key.required");
        AssertInvalid(typeMismatch, "lookup.key.type-mismatch");

        var invalidType = CreatePipeline(sourceSchema: [new EtlColumn("id", (EtlColumnType)99, Required: true)]);
        AssertInvalid(invalidType, "schema.field.type.invalid");

        var caseSensitiveNames = CreatePipeline(sourceSchema:
        [
            new EtlColumn("id", EtlColumnType.Int64, Required: true),
            new EtlColumn("ID", EtlColumnType.String, Required: false),
            new EtlColumn("amount", EtlColumnType.Decimal, Required: false),
            new EtlColumn("enabled", EtlColumnType.Boolean, Required: false)
        ]);
        Assert.Empty(Compile(caseSensitiveNames).Diagnostics);
    }

    [Fact]
    public void Compile_requires_a_unique_nonempty_mapping_of_declared_fields()
    {
        AssertInvalid(CreatePipeline(mapping: []), "mapping.required");
        AssertInvalid(CreatePipeline(mapping:
        [
            new EtlOutputField("value", EtlFieldOrigin.Source, "id"),
            new EtlOutputField("value", EtlFieldOrigin.Lookup, "label")
        ]), "mapping.name.duplicate");
        AssertInvalid(CreatePipeline(mapping: [new EtlOutputField("value", EtlFieldOrigin.Source, "missing")]), "mapping.field.missing");
    }

    [Fact]
    public void Compile_accepts_typed_filter_literals_and_null_for_an_optional_mapped_field()
    {
        using var nullValue = JsonDocument.Parse("null");
        using var decimalValue = JsonDocument.Parse("12.5");
        var pipeline = CreatePipeline(filters:
        [
            new EtlFilter("label", EtlComparison.Equal, nullValue.RootElement),
            new EtlFilter("amount", EtlComparison.NotEqual, decimalValue.RootElement)
        ]);

        var result = Compile(pipeline);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Artifact);
    }

    [Fact]
    public void Compile_rejects_filters_that_do_not_resolve_or_have_incompatible_literals()
    {
        using var stringValue = JsonDocument.Parse("\"not-a-number\"");
        using var nullValue = JsonDocument.Parse("null");
        using var integerValue = JsonDocument.Parse("1");
        var unresolved = CreatePipeline(filters: [new EtlFilter("notMapped", EtlComparison.Equal, stringValue.RootElement)]);
        var incompatible = CreatePipeline(filters: [new EtlFilter("amount", EtlComparison.Equal, stringValue.RootElement)]);
        var nullRequired = CreatePipeline(filters: [new EtlFilter("id", EtlComparison.Equal, nullValue.RootElement)]);
        var unsupportedComparison = CreatePipeline(filters: [new EtlFilter("id", (EtlComparison)99, integerValue.RootElement)]);

        AssertInvalid(unresolved, "filter.field.missing");
        AssertInvalid(incompatible, "filter.value.incompatible");
        AssertInvalid(nullRequired, "filter.value.null-required");
        AssertInvalid(unsupportedComparison, "filter.comparison.unsupported");
    }

    [Fact]
    public void Compile_enforces_positive_finite_budgets_and_cross_limit_relationships()
    {
        var limits = new EtlLimits
        {
            PartitionCount = 0,
            MaxRecordBytes = 10,
            MaxFields = 0,
            MaxBatchRecords = 10,
            MaxBatchBytes = 9,
            MaxOutstandingRecords = 9,
            MaxOutstandingBytes = 8,
            MaxLookupBytes = 11,
            MaxSnapshotBytes = 10,
            MaxArtifactBytes = 256 * 1024 + 1
        };

        var result = Compile(CreatePipeline(limits: limits));

        Assert.Null(result.Artifact);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "limits.invalid");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "limits.artifact-too-large");

        var tooFewFields = CreatePipeline(limits: new EtlLimits { MaxFields = 1 });
        AssertInvalid(tooFewFields, "limits.invalid");
    }

    [Fact]
    public void Compile_measures_the_complete_artifact_envelope_at_the_exact_configured_boundary()
    {
        var pipeline = CreatePipeline();
        var baseline = Assert.IsType<EtlArtifact>(Compile(pipeline).Artifact);
        var limit = JsonSerializer.SerializeToUtf8Bytes(baseline).Length;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var limitedPipeline = pipeline with { Limits = new EtlLimits { MaxArtifactBytes = limit } };
            var document = EtlArtifactJson.Write(limitedPipeline);
            var probe = new EtlArtifact(
                EtlArtifact.CurrentFormatVersion,
                new string('a', baseline.ArtifactId.Length),
                new string('a', baseline.ContentHash.Length),
                baseline.DefinitionId,
                baseline.DefinitionVersion,
                document);
            limit = JsonSerializer.SerializeToUtf8Bytes(probe).Length;
        }

        var exact = Compile("orders", 4, pipeline with { Limits = new EtlLimits { MaxArtifactBytes = limit } });
        var oneByteShort = Compile("orders", 4, pipeline with { Limits = new EtlLimits { MaxArtifactBytes = limit - 1 } });

        Assert.NotNull(exact.Artifact);
        Assert.Null(oneByteShort.Artifact);
        Assert.Contains(oneByteShort.Diagnostics, diagnostic => diagnostic.Code == "artifact.too-large");
    }

    [Fact]
    public void Diagnostics_are_bounded_and_never_echo_authored_values()
    {
        const string secret = "private-field-name";
        var invalidSchema = Enumerable.Range(0, 64)
            .Select(_ => new EtlColumn(secret, (EtlColumnType)99, Required: true))
            .ToArray();

        var result = Compile(CreatePipeline(sourceSchema: invalidSchema));

        Assert.Null(result.Artifact);
        Assert.True(result.Diagnostics.Count <= 32);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "diagnostics.truncated");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Message.Contains(secret, StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Path.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_rejects_oversized_input_before_writing_the_artifact_and_keeps_diagnostics_safe()
    {
        const string secret = "private-field-name-that-must-not-appear";
        var pipeline = CreatePipeline(mapping: [new EtlOutputField(secret + new string('x', 300_000), EtlFieldOrigin.Source, "id")]);

        var result = Compile(pipeline);

        Assert.Null(result.Artifact);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "artifact.too-large");
        Assert.All(result.Diagnostics, diagnostic =>
        {
            Assert.DoesNotContain(secret, diagnostic.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, diagnostic.Path, StringComparison.Ordinal);
        });
    }

    private static EtlCompilationResult Compile(EtlPipeline pipeline) => Compile("orders", 4, pipeline);

    private static EtlCompilationResult Compile(string definitionId, int version, EtlPipeline pipeline) =>
        new EtlArtifactCompiler().Compile(definitionId, version, pipeline);

    private static void AssertInvalid(EtlPipeline pipeline, string code)
    {
        var result = Compile(pipeline);

        Assert.Null(result.Artifact);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    private static EtlPipeline CreatePipeline(
        EtlFileBindings? bindings = null,
        IReadOnlyList<EtlColumn>? sourceSchema = null,
        IReadOnlyList<EtlColumn>? lookupSchema = null,
        IReadOnlyList<EtlOutputField>? mapping = null,
        IReadOnlyList<EtlFilter>? filters = null,
        EtlLimits? limits = null) =>
        new(
            bindings ?? new EtlFileBindings("source", "lookup", "output", "errors"),
            sourceSchema ??
            [
                new EtlColumn("id", EtlColumnType.Int64, Required: true),
                new EtlColumn("amount", EtlColumnType.Decimal, Required: false),
                new EtlColumn("enabled", EtlColumnType.Boolean, Required: false)
            ],
            lookupSchema ??
            [
                new EtlColumn("key", EtlColumnType.Int64, Required: true),
                new EtlColumn("label", EtlColumnType.String, Required: false)
            ],
            "id",
            "key",
            mapping ??
            [
                new EtlOutputField("id", EtlFieldOrigin.Source, "id"),
                new EtlOutputField("amount", EtlFieldOrigin.Source, "amount"),
                new EtlOutputField("label", EtlFieldOrigin.Lookup, "label")
            ],
            filters ?? [],
            limits ?? new EtlLimits());
}
