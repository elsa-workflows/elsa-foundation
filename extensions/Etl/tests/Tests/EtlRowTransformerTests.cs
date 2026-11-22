using System.Globalization;
using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Csv;
using Elsa.Etl.Runtime.Transformation;

namespace Elsa.Etl.Tests;

public sealed class EtlRowTransformerTests
{
    private static readonly IReadOnlyList<EtlColumn> SourceSchema =
    [
        new("sku", EtlColumnType.String, Required: true),
        new("quantity", EtlColumnType.Int64, Required: true),
        new("optional", EtlColumnType.String, Required: false)
    ];

    private static readonly IReadOnlyList<EtlColumn> LookupSchema =
    [
        new("sku", EtlColumnType.String, Required: true),
        new("label", EtlColumnType.String, Required: true),
        new("price", EtlColumnType.Decimal, Required: true),
        new("enabled", EtlColumnType.Boolean, Required: true)
    ];

    private static readonly IReadOnlyList<EtlOutputField> Mapping =
    [
        new("code", EtlFieldOrigin.Source, "sku"),
        new("quantity", EtlFieldOrigin.Source, "quantity"),
        new("label", EtlFieldOrigin.Lookup, "label"),
        new("price", EtlFieldOrigin.Lookup, "price"),
        new("enabled", EtlFieldOrigin.Lookup, "enabled"),
        new("optional", EtlFieldOrigin.Source, "optional")
    ];

    [Fact]
    public void Header_map_accepts_schema_fields_in_a_different_order()
    {
        var header = CompileHeader(SourceSchema, new CsvRecord(0, ["quantity", "sku", "optional"]));

        Assert.Equal(1, header.GetFieldIndex("sku"));
        Assert.Equal(0, header.GetFieldIndex("quantity"));
        Assert.Equal(2, header.GetFieldIndex("optional"));
    }

    [Fact]
    public void Header_map_rejects_duplicates_missing_fields_extra_fields_case_mismatch_and_nonzero_ordinal()
    {
        AssertHeaderError(["sku", "sku", "optional"], "etl.header-duplicate-field", "sku");
        AssertHeaderError(["sku", "quantity"], "etl.header-field-missing", "optional");
        AssertHeaderError(["sku", "quantity", "extra"], "etl.header-field-unexpected", null);
        AssertHeaderError(["SKU", "quantity", "optional"], "etl.header-field-unexpected", null);

        Assert.False(EtlCsvHeaderMap.TryCreate(SourceSchema, new CsvRecord(1, ["sku", "quantity", "optional"]), out _, out var ordinalError));
        Assert.Equal("etl.header-ordinal-invalid", ordinalError!.Code);
    }

    [Fact]
    public void Transforms_typed_source_and_lookup_values_to_compact_ordered_jsonl()
    {
        var pipeline = CreatePipeline(
        [
            Filter("quantity", EtlComparison.Equal, "2"),
            Filter("label", EtlComparison.NotEqual, "\"blocked\"")
        ]);
        var transformer = CreateTransformer(pipeline, new CsvRecord(1, ["tool", "p-1", "true", "12.5"]));

        var result = transformer.Transform(new CsvRecord(7, ["2", "p-1", ""]), 256);

        Assert.Equal(EtlRowDisposition.Accepted, result.Disposition);
        Assert.Null(result.Error);
        Assert.Equal(
            "{\"code\":\"p-1\",\"quantity\":2,\"label\":\"tool\",\"price\":12.5,\"enabled\":true,\"optional\":null}\n",
            Encoding.UTF8.GetString(result.JsonlBytes.Span));
    }

    [Fact]
    public void Filters_use_mapped_output_names_and_can_return_without_serializing()
    {
        var mapping = Mapping.Select(field => field with { Name = field.Name == "quantity" ? "units" : field.Name }).ToArray();
        var pipeline = CreatePipeline([Filter("units", EtlComparison.Equal, "2")], mapping);
        var transformer = CreateTransformer(pipeline, new CsvRecord(1, ["tool", "p-1", "true", "12.5"]));

        var result = transformer.Transform(new CsvRecord(11, ["3", "p-1", "available"]), 256);

        Assert.Equal(EtlRowDisposition.Filtered, result.Disposition);
        Assert.True(result.JsonlBytes.IsEmpty);
        Assert.Null(result.Error);
    }

    [Fact]
    public void String_filters_use_exact_ordinal_comparison()
    {
        var pipeline = CreatePipeline([Filter("label", EtlComparison.Equal, "\"TOOL\"")]);
        var transformer = CreateTransformer(pipeline, new CsvRecord(1, ["tool", "p-1", "true", "12.5"]));

        var result = transformer.Transform(new CsvRecord(1, ["2", "p-1", "available"]), 256);

        Assert.Equal(EtlRowDisposition.Filtered, result.Disposition);
    }

    [Fact]
    public void Typed_row_helper_parses_lookup_values_and_maps_optional_empty_to_null()
    {
        var parsed = EtlRowTransformer.TryParseTypedRow(
            SourceSchema,
            SourceHeader,
            new CsvRecord(3, ["2", "p-1", ""]),
            out var typedRow,
            out var error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.NotNull(typedRow);
        Assert.Equal(2L, typedRow.Values["quantity"].Value);
        Assert.Null(typedRow.Values["optional"].Value);

        Assert.True(EtlRowTransformer.TryParseTypedRow(
            LookupSchema,
            LookupHeader,
            new CsvRecord(1, ["tool", "p-1", "TrUe", "12.5"]),
            out var lookupRow,
            out error));
        Assert.Equal(12.5m, lookupRow!.Values["price"].Value);
        Assert.Equal(true, lookupRow.Values["enabled"].Value);
    }

    [Fact]
    public void Decimal_values_use_dot_decimal_independently_of_current_culture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            var parsed = EtlRowTransformer.TryParseTypedRow(
                LookupSchema,
                LookupHeader,
                new CsvRecord(2, ["tool", "p-1", "true", "12.5"]),
                out var lookupRow,
                out var error);

            Assert.True(parsed, error?.ToString());
            Assert.Equal(12.5m, lookupRow!.Values["price"].Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Required_blank_value_returns_safe_rejection_with_field_and_code()
    {
        var parsed = EtlRowTransformer.TryParseTypedRow(
            SourceSchema,
            SourceHeader,
            new CsvRecord(9, ["", "p-1", "private row content"]),
            out var typedRow,
            out var error);

        Assert.False(parsed);
        Assert.Null(typedRow);
        Assert.Equal(9, error!.RecordOrdinal);
        Assert.Equal("quantity", error.Field);
        Assert.Equal("etl.required-value-missing", error.Code);
        Assert.DoesNotContain("private row content", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EtlColumnType.Int64, "9223372036854775808")]
    [InlineData(EtlColumnType.Decimal, "1,25")]
    [InlineData(EtlColumnType.Decimal, "1e3")]
    [InlineData(EtlColumnType.Boolean, " true ")]
    [InlineData(EtlColumnType.Boolean, "yes")]
    public void Typed_row_helper_rejects_overflow_and_non_invariant_or_noncanonical_values(EtlColumnType type, string value)
    {
        var schema = new[] { new EtlColumn("value", type, Required: true) };

        var parsed = EtlRowTransformer.TryParseTypedRow(schema, CompileHeader(schema, new CsvRecord(0, ["value"])), new CsvRecord(4, [value]), out var typedRow, out var error);

        Assert.False(parsed);
        Assert.Null(typedRow);
        Assert.Equal(4, error!.RecordOrdinal);
        Assert.Equal("value", error.Field);
        Assert.Equal("etl.invalid-value", error.Code);
    }

    [Fact]
    public void Rejects_a_blank_or_missing_lookup_key_separately_from_row_conversion()
    {
        var pipeline = CreatePipeline([], sourceSchema: [
            new("sku", EtlColumnType.String, Required: false),
            new("quantity", EtlColumnType.Int64, Required: true),
            new("optional", EtlColumnType.String, Required: false)
        ]);
        var transformer = CreateTransformer(pipeline, new CsvRecord(1, ["tool", "p-1", "true", "12.5"]));

        var blank = transformer.Transform(new CsvRecord(1, ["2", "", "available"]), 256);
        var unknown = transformer.Transform(new CsvRecord(2, ["2", "absent", "available"]), 256);

        Assert.Equal(EtlRowDisposition.Rejected, blank.Disposition);
        Assert.Equal("etl.lookup-key-missing", blank.Error!.Code);
        Assert.Equal("sku", blank.Error.Field);
        Assert.Equal(EtlRowDisposition.Rejected, unknown.Disposition);
        Assert.Equal("etl.lookup-key-not-found", unknown.Error!.Code);
        Assert.Equal("sku", unknown.Error.Field);
    }

    [Fact]
    public void String_lookup_keys_use_exact_ordinal_match()
    {
        var transformer = CreateTransformer(CreatePipeline([]), new CsvRecord(1, ["tool", "p-1", "true", "12.5"]));

        var result = transformer.Transform(new CsvRecord(2, ["2", "P-1", "available"]), 256);

        Assert.Equal(EtlRowDisposition.Rejected, result.Disposition);
        Assert.Equal("etl.lookup-key-not-found", result.Error!.Code);
        Assert.Equal("sku", result.Error.Field);
    }

    [Fact]
    public void String_lookup_keys_preserve_nonempty_whitespace_and_match_exactly()
    {
        var transformer = CreateTransformer(CreatePipeline([]), new CsvRecord(1, ["tool", " ", "true", "12.5"]));

        var result = transformer.Transform(new CsvRecord(2, ["2", " ", "available"]), 256);

        Assert.Equal(EtlRowDisposition.Accepted, result.Disposition);
        Assert.Equal(
            "{\"code\":\" \",\"quantity\":2,\"label\":\"tool\",\"price\":12.5,\"enabled\":true,\"optional\":\"available\"}\n",
            Encoding.UTF8.GetString(result.JsonlBytes.Span));
    }

    [Fact]
    public void Lookup_parse_helper_rejects_invalid_typed_values_before_the_run_starts()
    {
        var parsed = EtlRowTransformer.TryParseTypedRow(
            LookupSchema,
            LookupHeader,
            new CsvRecord(4, ["tool", "p-1", "true", "NaN"]),
            out var typedRow,
            out var error);

        Assert.False(parsed);
        Assert.Null(typedRow);
        Assert.Equal("price", error!.Field);
        Assert.Equal("etl.invalid-value", error.Code);
    }

    [Fact]
    public void Enforces_output_limit_before_json_expansion_and_counts_the_line_feed()
    {
        var pipeline = CreatePipeline([], mapping: [new("x", EtlFieldOrigin.Source, "quantity")]);
        var transformer = CreateTransformer(pipeline, new CsvRecord(1, ["tool", "p-1", "true", "12.5"]));
        var source = new CsvRecord(6, ["1", "p-1", ""]);

        var exact = transformer.Transform(source, 8);
        var tooSmall = transformer.Transform(source, 7);

        Assert.Equal(EtlRowDisposition.Accepted, exact.Disposition);
        Assert.Equal("{\"x\":1}\n", Encoding.UTF8.GetString(exact.JsonlBytes.Span));
        Assert.Equal(EtlRowDisposition.Rejected, tooSmall.Disposition);
        Assert.True(tooSmall.JsonlBytes.IsEmpty);
        Assert.Equal("etl.output-too-large", tooSmall.Error!.Code);
        Assert.Equal(6, tooSmall.Error.RecordOrdinal);
    }

    [Fact]
    public void Rejects_json_string_expansion_before_output_buffer_growth_exceeds_the_remaining_budget()
    {
        var pipeline = CreatePipeline([], mapping: [new("value", EtlFieldOrigin.Source, "optional")]);
        var transformer = CreateTransformer(pipeline, new CsvRecord(1, ["tool", "p-1", "true", "12.5"]));

        var result = transformer.Transform(new CsvRecord(5, ["2", "p-1", new string('\u0001', 32)]), 16);

        Assert.Equal(EtlRowDisposition.Rejected, result.Disposition);
        Assert.True(result.JsonlBytes.IsEmpty);
        Assert.Equal("etl.output-too-large", result.Error!.Code);
        Assert.Equal("value", result.Error.Field);
    }

    [Fact]
    public void Does_not_treat_the_header_record_as_a_source_data_row()
    {
        var transformer = CreateTransformer(CreatePipeline([]), new CsvRecord(1, ["tool", "p-1", "true", "12.5"]));

        var result = transformer.Transform(new CsvRecord(0, ["quantity", "sku", "optional"]), 256);

        Assert.Equal(EtlRowDisposition.Rejected, result.Disposition);
        Assert.Equal("etl.header-record-not-data", result.Error!.Code);
        Assert.Equal(0, result.Error.RecordOrdinal);
    }

    [Fact]
    public void Rejects_source_record_shape_mismatch_without_exposing_values()
    {
        var transformer = CreateTransformer(CreatePipeline([]), new CsvRecord(1, ["tool", "p-1", "true", "12.5"]));

        var result = transformer.Transform(new CsvRecord(13, ["private raw value"]), 256);

        Assert.Equal(EtlRowDisposition.Rejected, result.Disposition);
        Assert.Equal("etl.field-count-mismatch", result.Error!.Code);
        Assert.Null(result.Error.Field);
        Assert.Equal(13, result.Error.RecordOrdinal);
        Assert.DoesNotContain("private raw value", result.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_filter_literals_that_disagree_with_the_mapped_output_type()
    {
        var pipeline = CreatePipeline([Filter("quantity", EtlComparison.Equal, "\"2\"")]);

        Assert.Throws<ArgumentException>(() => new EtlRowTransformer(pipeline, SourceHeader, CreateLookup(new CsvRecord(1, ["tool", "p-1", "true", "12.5"]))));
    }

    private static EtlRowTransformer CreateTransformer(EtlPipeline pipeline, CsvRecord lookupRecord) =>
        new(pipeline, SourceHeader, CreateLookup(lookupRecord));

    private static EtlCsvHeaderMap SourceHeader => CompileHeader(SourceSchema, new CsvRecord(0, ["quantity", "sku", "optional"]));

    private static EtlCsvHeaderMap LookupHeader => CompileHeader(LookupSchema, new CsvRecord(0, ["label", "sku", "enabled", "price"]));

    private static EtlCsvHeaderMap CompileHeader(IReadOnlyList<EtlColumn> schema, CsvRecord headerRecord)
    {
        Assert.True(EtlCsvHeaderMap.TryCreate(schema, headerRecord, out var header, out var error), error?.ToString());
        return header!;
    }

    private static void AssertHeaderError(string[] fields, string code, string? field)
    {
        var parsed = EtlCsvHeaderMap.TryCreate(SourceSchema, new CsvRecord(0, fields), out var header, out var error);

        Assert.False(parsed);
        Assert.Null(header);
        Assert.Equal(code, error!.Code);
        Assert.Equal(field, error.Field);
        Assert.Equal(0, error.RecordOrdinal);
    }

    private static Dictionary<EtlTypedValue, EtlTypedRow> CreateLookup(CsvRecord record)
    {
        Assert.True(EtlRowTransformer.TryParseTypedRow(LookupSchema, LookupHeader, record, out var row, out var error), error?.ToString());
        Assert.NotNull(row);
        var key = row.Values["sku"];
        Assert.NotNull(key.Value);
        return new Dictionary<EtlTypedValue, EtlTypedRow> { [key] = row };
    }

    private static EtlPipeline CreatePipeline(
        IReadOnlyList<EtlFilter> filters,
        IReadOnlyList<EtlOutputField>? mapping = null,
        IReadOnlyList<EtlColumn>? sourceSchema = null) =>
        new(
            new EtlFileBindings("source", "lookup", "output", "errors"),
            sourceSchema ?? SourceSchema,
            LookupSchema,
            "sku",
            "sku",
            mapping ?? Mapping,
            filters,
            new EtlLimits());

    private static EtlFilter Filter(string field, EtlComparison comparison, string json) =>
        new(field, comparison, JsonValue(json));

    private static JsonElement JsonValue(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
