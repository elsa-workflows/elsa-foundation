using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Csv;
using Elsa.Etl.Runtime.Transformation;

namespace Elsa.Etl.Tests;

public sealed class EtlConnectorTransformTests
{
    [Fact]
    public void Http_json_typed_records_transform_without_a_synthetic_lookup_or_csv_record()
    {
        var pipeline = EtlV2TestPipelines.Create() with
        {
            Mapping = [new EtlOutputField("displayName", EtlFieldOrigin.Source, "name")]
        };
        EtlRowTransformer.ValidateV2Semantics(pipeline);
        var transformer = new EtlRowTransformer(pipeline);

        var result = transformer.Transform(SourceRow(4, 17L, "Ada"), 128);

        Assert.Equal(EtlRowDisposition.Accepted, result.Disposition);
        Assert.Equal("{\"displayName\":\"Ada\"}\n", Encoding.UTF8.GetString(result.JsonlBytes.Span));
    }

    [Fact]
    public void V2_optional_nulls_are_serialized_and_filters_return_zero_bytes()
    {
        var baseline = EtlV2TestPipelines.Create();
        var source = (EtlHttpJsonSource)baseline.Source;
        var pipeline = baseline with
        {
            Source = source with { Fields = [source.Fields[0], source.Fields[1] with { Required = false }] },
            Filters = [Filter("name", EtlComparison.Equal, "null")]
        };
        var transformer = new EtlRowTransformer(pipeline);

        var filtered = transformer.Transform(SourceRow(1, 5L, "Ada"), 128);
        var acceptedPipeline = pipeline with { Filters = [] };
        var acceptedTransformer = new EtlRowTransformer(acceptedPipeline);
        var accepted = acceptedTransformer.Transform(SourceRow(1, 5L, null), 128);
        var missingOptional = acceptedTransformer.Transform(new EtlTypedRow(2, Values(("id", EtlColumnType.Int64, 5L))), 128);

        Assert.Equal(EtlRowDisposition.Filtered, filtered.Disposition);
        Assert.True(filtered.JsonlBytes.IsEmpty);
        Assert.Equal(EtlRowDisposition.Accepted, accepted.Disposition);
        Assert.Equal("{\"name\":null}\n", Encoding.UTF8.GetString(accepted.JsonlBytes.Span));
        Assert.Equal(accepted.JsonlBytes.ToArray(), missingOptional.JsonlBytes.ToArray());
    }

    [Fact]
    public void V2_lookup_joins_typed_rows_and_maps_fields_from_both_origins()
    {
        var baseline = EtlV2TestPipelines.Create();
        var pipeline = baseline with
        {
            Lookup = new EtlCsvLookup(
                "lookup",
                [new EtlColumn("id", EtlColumnType.Int64, Required: true), new EtlColumn("tier", EtlColumnType.String, Required: true)],
                "id",
                "id"),
            Mapping =
            [
                new EtlOutputField("name", EtlFieldOrigin.Source, "name"),
                new EtlOutputField("tier", EtlFieldOrigin.Lookup, "tier")
            ]
        };
        var key = new EtlTypedValue(EtlColumnType.Int64, 17L);
        var lookupRow = new EtlTypedRow(1, Values(
            ("id", EtlColumnType.Int64, 17L),
            ("tier", EtlColumnType.String, "gold")));
        var transformer = new EtlRowTransformer(pipeline, new Dictionary<EtlTypedValue, EtlTypedRow> { [key] = lookupRow });

        var result = transformer.Transform(SourceRow(3, 17L, "Ada"), 128);

        Assert.Equal(EtlRowDisposition.Accepted, result.Disposition);
        Assert.Equal("{\"name\":\"Ada\",\"tier\":\"gold\"}\n", Encoding.UTF8.GetString(result.JsonlBytes.Span));
    }

    [Fact]
    public void V2_transform_rejects_required_nulls_wrong_clr_scalar_types_and_missing_required_fields_safely()
    {
        var pipeline = EtlV2TestPipelines.Create();
        var transformer = new EtlRowTransformer(pipeline);

        var requiredNull = transformer.Transform(SourceRow(2, null, "Ada"), 128);
        var wrongClrType = transformer.Transform(new EtlTypedRow(3, Values(
            ("id", EtlColumnType.Int64, (object)17),
            ("name", EtlColumnType.String, "Ada"))), 128);
        var missingDeclaredField = transformer.Transform(new EtlTypedRow(4, Values(
            ("id", EtlColumnType.Int64, 17L))), 128);

        Assert.Equal("etl.required-value-missing", requiredNull.Error!.Code);
        Assert.Equal("id", requiredNull.Error.Field);
        Assert.Equal("etl.invalid-value", wrongClrType.Error!.Code);
        Assert.Equal("id", wrongClrType.Error.Field);
        Assert.Equal("etl.required-value-missing", missingDeclaredField.Error!.Code);
        Assert.Equal("name", missingDeclaredField.Error.Field);
        Assert.DoesNotContain("Ada", wrongClrType.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void V2_typed_scalar_validation_and_serialization_keep_decimal_and_boolean_types()
    {
        var baseline = EtlV2TestPipelines.Create();
        var source = (EtlHttpJsonSource)baseline.Source;
        var pipeline = baseline with
        {
            Source = source with
            {
                Fields =
                [
                    new EtlJsonField("price", "/price", EtlColumnType.Decimal, Required: true),
                    new EtlJsonField("active", "/active", EtlColumnType.Boolean, Required: true)
                ]
            },
            Mapping =
            [
                new EtlOutputField("price", EtlFieldOrigin.Source, "price"),
                new EtlOutputField("active", EtlFieldOrigin.Source, "active")
            ]
        };
        var transformer = new EtlRowTransformer(pipeline);
        var valid = transformer.Transform(new EtlTypedRow(1, Values(
            ("price", EtlColumnType.Decimal, 12.5m),
            ("active", EtlColumnType.Boolean, true))), 128);
        var wrongRuntimeType = transformer.Transform(new EtlTypedRow(2, Values(
            ("price", EtlColumnType.Decimal, 12.5d),
            ("active", EtlColumnType.Boolean, true))), 128);

        Assert.Equal("{\"price\":12.5,\"active\":true}\n", Encoding.UTF8.GetString(valid.JsonlBytes.Span));
        Assert.Equal("etl.invalid-value", wrongRuntimeType.Error!.Code);
        Assert.Equal("price", wrongRuntimeType.Error.Field);
    }

    [Fact]
    public void V2_semantics_requires_both_lookup_key_fields_to_be_required()
    {
        var baseline = EtlV2TestPipelines.Create();
        var source = (EtlHttpJsonSource)baseline.Source;
        var optionalSourceKey = baseline with
        {
            Source = source with { Fields = [source.Fields[0] with { Required = false }, source.Fields[1]] },
            Lookup = new EtlCsvLookup("lookup", [new EtlColumn("id", EtlColumnType.Int64, Required: true)], "id", "id")
        };
        var optionalLookupKey = baseline with
        {
            Lookup = new EtlCsvLookup("lookup", [new EtlColumn("id", EtlColumnType.Int64, Required: false)], "id", "id")
        };

        Assert.Throws<ArgumentException>(() => EtlRowTransformer.ValidateV2Semantics(optionalSourceKey));
        Assert.Throws<ArgumentException>(() => EtlRowTransformer.ValidateV2Semantics(optionalLookupKey));
    }

    [Fact]
    public void V2_semantic_validation_rejects_unmapped_fields_and_incompatible_filter_literals()
    {
        var pipeline = EtlV2TestPipelines.Create();
        var unknownMapping = pipeline with { Mapping = [new EtlOutputField("label", EtlFieldOrigin.Source, "missing")] };
        var incompatibleFilter = pipeline with { Filters = [Filter("name", EtlComparison.Equal, "17")] };
        var nullRequiredFilter = pipeline with { Filters = [Filter("name", EtlComparison.Equal, "null")] };
        var lookupWithoutDeclaration = pipeline with
        {
            Mapping = [new EtlOutputField("tier", EtlFieldOrigin.Lookup, "tier")]
        };

        Assert.Throws<ArgumentException>(() => EtlRowTransformer.ValidateV2Semantics(unknownMapping));
        Assert.Throws<ArgumentException>(() => EtlRowTransformer.ValidateV2Semantics(incompatibleFilter));
        Assert.Throws<ArgumentException>(() => EtlRowTransformer.ValidateV2Semantics(nullRequiredFilter));
        Assert.Throws<ArgumentException>(() => EtlRowTransformer.ValidateV2Semantics(lookupWithoutDeclaration));
    }

    [Fact]
    public void V2_lookup_requires_matching_typed_keys_and_a_prepared_snapshot()
    {
        var baseline = EtlV2TestPipelines.Create();
        var pipeline = baseline with
        {
            Lookup = new EtlCsvLookup(
                "lookup",
                [new EtlColumn("id", EtlColumnType.String, Required: true)],
                "id",
                "id")
        };

        Assert.Throws<ArgumentException>(() => EtlRowTransformer.ValidateV2Semantics(pipeline));

        var matchingPipeline = baseline with
        {
            Lookup = new EtlCsvLookup(
                "lookup",
                [new EtlColumn("id", EtlColumnType.Int64, Required: true)],
                "id",
                "id")
        };
        Assert.Throws<ArgumentNullException>(() => new EtlRowTransformer(matchingPipeline));
    }

    [Fact]
    public void V2_output_budget_is_enforced_before_returning_a_partial_row()
    {
        var pipeline = EtlV2TestPipelines.Create();
        var result = new EtlRowTransformer(pipeline).Transform(SourceRow(1, 17L, "Ada"), 1);

        Assert.Equal(EtlRowDisposition.Rejected, result.Disposition);
        Assert.True(result.JsonlBytes.IsEmpty);
        Assert.Equal("etl.output-too-large", result.Error!.Code);
        Assert.Equal("name", result.Error.Field);
    }

    [Fact]
    public void V2_and_v1_file_transformers_preserve_the_compact_jsonl_output_shape()
    {
        var v1 = EtlTestPipelines.Create();
        Assert.True(EtlRowTransformer.TryCompileHeader(v1.SourceSchema, new CsvRecord(0, ["id", "name"]), out var sourceHeader, out var headerError), headerError?.ToString());
        Assert.True(EtlRowTransformer.TryCompileHeader(v1.LookupSchema, new CsvRecord(0, ["id", "category"]), out var lookupHeader, out headerError), headerError?.ToString());
        Assert.True(EtlRowTransformer.TryParseTypedRow(v1.LookupSchema, lookupHeader!, new CsvRecord(1, ["17", "gold"]), out var lookupRow, out var lookupError), lookupError?.ToString());
        var parsedLookupRow = lookupRow!;
        var lookupKey = parsedLookupRow.Values["id"];
        var v1Transformer = new EtlRowTransformer(v1, sourceHeader!, new Dictionary<EtlTypedValue, EtlTypedRow> { [lookupKey] = parsedLookupRow });
        var v1Result = v1Transformer.Transform(new CsvRecord(2, ["17", "Ada"]), 128);

        var v2Transformer = new EtlRowTransformer(EtlV2TestPipelines.Create());
        var v2Result = v2Transformer.Transform(SourceRow(2, 17L, "Ada"), 128);
        var csvSource = new EtlCsvSource("source",
            [new EtlColumn("id", EtlColumnType.Int64, Required: true), new EtlColumn("name", EtlColumnType.String, Required: true)]);
        var v2CsvTransformer = new EtlRowTransformer(EtlV2TestPipelines.Create() with { Source = csvSource });
        Assert.True(EtlRowTransformer.TryCompileHeader(csvSource.Schema, new CsvRecord(0, ["id", "name"]), out var v2CsvHeader, out headerError), headerError?.ToString());
        Assert.True(EtlRowTransformer.TryParseTypedRow(csvSource.Schema, v2CsvHeader!, new CsvRecord(2, ["17", "Ada"]), out var v2CsvRow, out lookupError), lookupError?.ToString());
        var v2CsvResult = v2CsvTransformer.Transform(v2CsvRow!, 128);

        Assert.Equal(EtlRowDisposition.Accepted, v1Result.Disposition);
        Assert.Equal(v1Result.JsonlBytes.ToArray(), v2Result.JsonlBytes.ToArray());
        Assert.Equal(v1Result.JsonlBytes.ToArray(), v2CsvResult.JsonlBytes.ToArray());
        Assert.Equal("{\"name\":\"Ada\"}\n", Encoding.UTF8.GetString(v1Result.JsonlBytes.Span));
    }

    private static EtlTypedRow SourceRow(int ordinal, long? id, string? name) =>
        new(ordinal, Values(
            ("id", EtlColumnType.Int64, id),
            ("name", EtlColumnType.String, name)));

    private static Dictionary<string, EtlTypedValue> Values(params (string Name, EtlColumnType Type, object? Value)[] fields) =>
        fields.ToDictionary(field => field.Name, field => new EtlTypedValue(field.Type, field.Value), StringComparer.Ordinal);

    private static EtlFilter Filter(string field, EtlComparison comparison, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new(field, comparison, document.RootElement);
    }
}
