using System.Globalization;
using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Http;
using Elsa.Etl.Runtime.Transformation;

namespace Elsa.Etl.Tests;

public sealed class EtlJsonPageReaderTests
{
    [Fact]
    public void Reads_typed_rows_with_absolute_indices_and_raw_record_byte_counts()
    {
        const string record = "{\"id\":9223372036854775807,\"name\":\"Ada\"}";
        const string page = "{\"records\":[{\"id\":9223372036854775807,\"name\":\"Ada\"}],\"next\":\"cursor-2\"}";

        var result = Read(page, firstRecordOrdinal: 41);

        var item = Assert.Single(result.Records);
        Assert.Equal(41, item.Index);
        Assert.Equal(Encoding.UTF8.GetByteCount(record), item.RawInputBytes);
        Assert.Null(item.Error);
        Assert.Equal(1, item.Row!.RecordOrdinal);
        Assert.Equal(long.MaxValue, item.Row.Values["id"].Value);
        Assert.Equal("Ada", item.Row.Values["name"].Value);
        Assert.Equal("cursor-2", result.Continuation);
    }

    [Fact]
    public void Reads_strict_int64_decimal_boolean_and_optional_null_or_missing_values()
    {
        var source = Source([
            new EtlJsonField("id", "/id", EtlColumnType.Int64, Required: true),
            new EtlJsonField("amount", "/amount", EtlColumnType.Decimal, Required: true),
            new EtlJsonField("active", "/active", EtlColumnType.Boolean, Required: true),
            new EtlJsonField("note", "/note", EtlColumnType.String, Required: false)
        ]);

        var result = Read("{\"records\":[{\"id\":9,\"amount\":12.5,\"active\":true,\"note\":null},{\"id\":10,\"amount\":0.25,\"active\":false}]}", source);

        Assert.Equal(2, result.Records.Count);
        Assert.Equal(9L, result.Records[0].Row!.Values["id"].Value);
        Assert.Equal(12.5m, result.Records[0].Row!.Values["amount"].Value);
        Assert.Equal(true, result.Records[0].Row!.Values["active"].Value);
        Assert.Null(result.Records[0].Row!.Values["note"].Value);
        Assert.Null(result.Records[1].Error);
        Assert.Null(result.Records[1].Row!.Values["note"].Value);
    }

    [Fact]
    public void Required_missing_and_null_fields_produce_safe_row_errors()
    {
        var result = Read("{\"records\":[{\"id\":null},{}]}", Source([new EtlJsonField("id", "/id", EtlColumnType.Int64, Required: true)]));

        Assert.All(result.Records, item =>
        {
            Assert.Null(item.Row);
            Assert.Equal("etl.required-value-missing", item.Error!.Code);
            Assert.Equal("id", item.Error.Field);
        });
    }

    [Theory]
    [InlineData("9223372036854775808", EtlColumnType.Int64)]
    [InlineData("1.0", EtlColumnType.Int64)]
    [InlineData("1e1000", EtlColumnType.Decimal)]
    [InlineData("\"17\"", EtlColumnType.Int64)]
    [InlineData("{\"nested\":1}", EtlColumnType.String)]
    public void Ill_typed_and_out_of_range_scalars_are_row_errors_without_coercion(string rawValue, EtlColumnType type)
    {
        var source = Source([new EtlJsonField("value", "/value", type, Required: true)]);

        var item = Assert.Single(Read("{\"records\":[{\"value\":" + rawValue + "}]}", source).Records);

        Assert.Null(item.Row);
        Assert.Equal("etl.invalid-value", item.Error!.Code);
        Assert.Equal("value", item.Error.Field);
    }

    [Theory]
    [InlineData("79228162514264337593543950335", "79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335", "-79228162514264337593543950335")]
    [InlineData("1e-28", "1e-28")]
    [InlineData("0.0000000000000000000000000001", "1e-28")]
    [InlineData("1.2300e2", "123")]
    [InlineData("1000e-3", "1")]
    [InlineData("792281625142643375935439503350e-1", "79228162514264337593543950335")]
    public void Decimal_parser_preserves_exact_representable_values(string rawValue, string expected)
    {
        var actual = ReadDecimal(rawValue);

        Assert.Equal(decimal.Parse(expected, NumberStyles.Float, CultureInfo.InvariantCulture), actual);
    }

    [Fact]
    public void Decimal_parser_accepts_negative_zero_and_equivalent_forms_with_many_trailing_zeroes()
    {
        var negativeZero = ReadDecimal("-0e-" + new string('9', 120));
        var equivalentOne = ReadDecimal("1" + new string('0', 128) + "e-128");

        Assert.Equal(0m, negativeZero);
        Assert.Equal(int.MinValue, decimal.GetBits(negativeZero)[3] & int.MinValue);
        Assert.Equal(1m, equivalentOne);
    }

    [Theory]
    [InlineData("1e-29")]
    [InlineData("79228162514264337593543950335.1")]
    [InlineData("79228162514264337593543950336")]
    [InlineData("1.234567890123456789012345678901")]
    [InlineData("0.10000000000000000000000000001")]
    public void Decimal_parser_rejects_underflow_rounding_and_values_outside_decimal_range(string rawValue)
    {
        var item = ReadDecimalRecord(rawValue);

        Assert.Null(item.Row);
        Assert.Equal("etl.invalid-value", item.Error!.Code);
    }

    [Fact]
    public void Decimal_parser_saturates_extreme_exponents_without_overflow_or_expansion()
    {
        var extremeExponent = new string('9', 256);

        Assert.Equal("etl.invalid-value", ReadDecimalRecord("1e+" + extremeExponent).Error!.Code);
        Assert.Equal("etl.invalid-value", ReadDecimalRecord("1e-" + extremeExponent).Error!.Code);
        Assert.Equal(0m, ReadDecimal("-0e+" + extremeExponent));
    }

    [Fact]
    public void Decimal_values_written_by_the_platform_round_trip_at_every_supported_scale()
    {
        var coefficients = new[] { (1, 0, 0), (-1, -1, -1), (12345, -98765, 56789) };
        foreach (var (low, middle, high) in coefficients)
        for (byte scale = 0; scale <= 28; scale++)
        foreach (var negative in new[] { false, true })
        {
            var expected = new decimal(low, middle, high, negative, scale);
            Assert.Equal(expected, ReadDecimal(JsonSerializer.Serialize(expected)));
        }
    }

    [Fact]
    public void Missing_selected_field_and_oversized_record_are_rejected_individually()
    {
        var source = Source([new EtlJsonField("id", "/id", EtlColumnType.Int64, Required: true)]);
        var limits = new EtlConnectorLimits { MaxRecordBytes = 7 };

        var result = Read("{\"records\":[17,{\"id\":1}]}", source, limits);

        Assert.Equal("etl.required-value-missing", result.Records[0].Error!.Code);
        Assert.Equal("etl.record-too-large", result.Records[1].Error!.Code);
        Assert.Null(result.Records[0].Row);
        Assert.Null(result.Records[1].Row);
    }

    [Theory]
    [InlineData("17", "")]
    [InlineData("[17]", "/0")]
    [InlineData("{\"value\":[17]}", "/value/0")]
    public void Declared_pointer_selects_scalar_fields_from_any_record_shape(string record, string pointer)
    {
        var source = Source([new EtlJsonField("value", pointer, EtlColumnType.Int64, Required: true)]);

        var item = Assert.Single(Read("{\"records\":[" + record + "]}", source).Records);

        Assert.Null(item.Error);
        Assert.Equal(17L, item.Row!.Values["value"].Value);
    }

    [Fact]
    public void Accepts_exact_page_and_record_byte_limits_and_refuses_the_next_byte()
    {
        const string record = "{\"id\":1,\"name\":\"A\"}";
        const string page = "{\"records\":[{\"id\":1,\"name\":\"A\"}]}";
        var recordBytes = Encoding.UTF8.GetByteCount(record);
        var pageBytes = Encoding.UTF8.GetByteCount(page);

        var accepted = Read(page,
            limits: new EtlConnectorLimits { MaxRecordBytes = recordBytes },
            policy: new EtlHttpPolicy { MaxPageBytes = pageBytes, MaxPageRecords = 1 });
        Assert.Null(Assert.Single(accepted.Records).Error);

        var tooLargePage = Assert.Throws<EtlJsonPageReadException>(() => Read(page,
            policy: new EtlHttpPolicy { MaxPageBytes = pageBytes - 1 }));
        Assert.Equal("etl.http.page-too-large", tooLargePage.Code);
        var oversizedMalformedPage = Assert.Throws<EtlJsonPageReadException>(() => Read("not-json-and-too-long",
            policy: new EtlHttpPolicy { MaxPageBytes = 4 }));
        Assert.Equal("etl.http.page-too-large", oversizedMalformedPage.Code);

        var tooLargeRecord = Assert.Single(Read(page,
            limits: new EtlConnectorLimits { MaxRecordBytes = recordBytes - 1 }).Records);
        Assert.Equal("etl.record-too-large", tooLargeRecord.Error!.Code);
    }

    [Fact]
    public void Refuses_record_count_above_the_page_limit_before_returning_rows()
    {
        var exception = Assert.Throws<EtlJsonPageReadException>(() => Read("{\"records\":[{\"id\":1,\"name\":\"A\"},{\"id\":2,\"name\":\"B\"}]}",
            policy: new EtlHttpPolicy { MaxPageRecords = 1 }));

        Assert.Equal("etl.http.page-record-limit", exception.Code);
    }

    [Fact]
    public void Enforces_json_depth_at_the_configured_boundary()
    {
        var atLimit = NestedPage(4);
        var overLimit = NestedPage(5);
        var policy = new EtlHttpPolicy { MaxJsonDepth = 4 };

        Assert.Empty(Read(atLimit.Json, Source(recordsPointer: atLimit.Pointer), policy: policy).Records);
        var exception = Assert.Throws<EtlJsonPageReadException>(() => Read(overLimit.Json, Source(recordsPointer: overLimit.Pointer), policy: policy));
        Assert.Equal("etl.http.page-invalid", exception.Code);
    }

    [Theory]
    [InlineData("{\"kind\":\"one\",\"kind\":\"two\",\"records\":[]}")]
    [InlineData("{\"records\":[{\"id\":1,\"name\":\"Ada\",\"metadata\":{\"x\":1,\"x\":2}}]}")]
    public void Rejects_duplicate_properties_at_any_depth_including_metadata(string json)
    {
        var exception = Assert.Throws<EtlJsonPageReadException>(() => Read(json));

        Assert.Equal("etl.http.duplicate-property", exception.Code);
        Assert.DoesNotContain("Ada", exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"records\":[],\"next\":null}", null)]
    [InlineData("{\"records\":[],\"next\":\"\"}", null)]
    [InlineData("{\"records\":[]}", null)]
    [InlineData("{\"records\":[],\"next\":\"c2\"}", "c2")]
    public void Empty_page_is_not_eof_without_consulting_its_continuation(string json, string? continuation)
    {
        var result = Read(json);

        Assert.Empty(result.Records);
        Assert.Equal(continuation, result.Continuation);
    }

    [Theory]
    [InlineData("{\"records\":[],\"next\":7}", "7")]
    [InlineData("{\"records\":[],\"next\":false}", "false")]
    [InlineData("{\"records\":[],\"next\":\"token\"}", "token")]
    public void Token_continuation_accepts_scalar_text_without_coercing_structures(string json, string continuation)
    {
        Assert.Equal(continuation, Read(json).Continuation);
    }

    [Fact]
    public void Next_link_continuation_requires_a_string_and_continuation_is_bounded()
    {
        var nextLink = Source(pagination: new EtlNextLinkPagination("/next"));
        var invalidShape = Assert.Throws<EtlJsonPageReadException>(() => Read("{\"records\":[],\"next\":4}", nextLink));
        Assert.Equal("etl.http.continuation-invalid", invalidShape.Code);

        Assert.Equal("c2", Read("{\"records\":[],\"next\":\"c2\"}", policy: new EtlHttpPolicy { MaxContinuationBytes = 2 }).Continuation);

        var oversized = Assert.Throws<EtlJsonPageReadException>(() => Read("{\"records\":[],\"next\":\"long\"}",
            policy: new EtlHttpPolicy { MaxContinuationBytes = 3 }));
        Assert.Equal("etl.http.continuation-too-large", oversized.Code);
    }

    [Fact]
    public void Continuation_policy_is_capped_by_the_persisted_metadata_bound()
    {
        var unsupported = Assert.Throws<EtlJsonPageReadException>(() => Read("{\"records\":[]}",
            policy: new EtlHttpPolicy { MaxContinuationBytes = EtlHttpPolicy.MaximumSupportedContinuationBytes + 1 }));
        Assert.Equal("etl.http.page-policy-invalid", unsupported.Code);

        var continuation = new string('c', EtlHttpPolicy.MaximumSupportedContinuationBytes);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { records = Array.Empty<object>(), next = continuation }));
        var accepted = EtlJsonPageReader.Read(Encoding.UTF8.GetBytes(document.RootElement.GetRawText()), Source(),
            new EtlConnectorLimits(), new EtlHttpPolicy { MaxContinuationBytes = EtlHttpPolicy.MaximumSupportedContinuationBytes });
        Assert.Equal(continuation, accepted.Continuation);
    }

    [Fact]
    public void Single_page_profile_does_not_read_a_continuation_selector()
    {
        var source = Source(pagination: new EtlSinglePagePagination());

        var result = Read("{\"records\":[] ,\"next\":{\"invalid\":true}}", source);

        Assert.Empty(result.Records);
        Assert.Null(result.Continuation);
    }

    [Fact]
    public void Invalid_json_and_non_array_record_selectors_are_refused_with_safe_codes()
    {
        var malformed = Assert.Throws<EtlJsonPageReadException>(() => Read("{not-json}"));
        Assert.Equal("etl.http.page-invalid", malformed.Code);

        var wrongShape = Assert.Throws<EtlJsonPageReadException>(() => Read("{\"records\":{\"id\":1}}"));
        Assert.Equal("etl.http.records-invalid", wrongShape.Code);
    }

    [Theory]
    [InlineData("{\"records\":[{\"id\":1,\"name\":\"TEXT\"}]}")]
    [InlineData("{\"records\":[],\"ignored\":\"TEXT\"}")]
    [InlineData("{\"records\":[],\"TEXT\":0}")]
    public void Invalid_utf8_anywhere_in_the_page_is_refused_without_exposing_input(string template)
    {
        var bytes = Encoding.UTF8.GetBytes(template.Replace("TEXT", "x", StringComparison.Ordinal));
        bytes[Array.IndexOf(bytes, (byte)'x')] = 0xff;

        var exception = Assert.Throws<EtlJsonPageReadException>(() =>
            EtlJsonPageReader.Read(bytes, Source(), new EtlConnectorLimits(), new EtlHttpPolicy()));

        Assert.Equal("etl.http.page-invalid", exception.Code);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData("{\"records\":[{\"id\":1,\"name\":\"\\uD800\"}]}")]
    [InlineData("{\"records\":[],\"next\":\"\\uD800\"}")]
    [InlineData("{\"records\":[],\"\\uD800\":0}")]
    [InlineData("{\"records\":[],\"ignored\":\"\\uD800\"}")]
    public void Invalid_escaped_unicode_returns_a_safe_page_diagnostic(string json)
    {
        var exception = Assert.Throws<EtlJsonPageReadException>(() => Read(json));

        Assert.Equal("etl.http.page-invalid", exception.Code);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Valid_unicode_and_escaped_property_names_preserve_values_and_byte_limits()
    {
        const string json = "{\"recor\\u0064s\":[{\"id\":1,\"name\":\"\\uD83D\\uDE00\"}],\"next\":\"é\"}";

        var page = Read(json, policy: new EtlHttpPolicy { MaxContinuationBytes = 2 });

        Assert.Equal("😀", Assert.Single(page.Records).Row!.Values["name"].Value);
        Assert.Equal("é", page.Continuation);
        Assert.Equal("etl.http.continuation-too-large", Assert.Throws<EtlJsonPageReadException>(() =>
            Read(json, policy: new EtlHttpPolicy { MaxContinuationBytes = 1 })).Code);
        Assert.Equal("etl.http.duplicate-property", Assert.Throws<EtlJsonPageReadException>(() =>
            Read("{\"records\":[],\"recor\\u0064s\":[]}")).Code);
    }

    [Fact]
    public void Absolute_indices_accept_the_last_representable_position_but_never_wrap()
    {
        const string one = "{\"records\":[{\"id\":1,\"name\":\"A\"}]}";
        const string two = "{\"records\":[{\"id\":1,\"name\":\"A\"},{\"id\":2,\"name\":\"B\"}]}";

        Assert.Equal(long.MaxValue, Assert.Single(Read(one, firstRecordOrdinal: long.MaxValue).Records).Index);
        Assert.Equal("etl.http.position-invalid", Assert.Throws<EtlJsonPageReadException>(() =>
            Read(two, firstRecordOrdinal: long.MaxValue)).Code);
    }

    private static EtlJsonPage Read(
        string json,
        EtlHttpJsonSource? source = null,
        EtlConnectorLimits? limits = null,
        EtlHttpPolicy? policy = null,
        long firstRecordOrdinal = 0) =>
        EtlJsonPageReader.Read(Encoding.UTF8.GetBytes(json), source ?? Source(), limits ?? new EtlConnectorLimits(),
            policy ?? new EtlHttpPolicy(), firstRecordOrdinal);

    private static decimal ReadDecimal(string rawValue)
    {
        var item = ReadDecimalRecord(rawValue);
        Assert.Null(item.Error);
        return Assert.IsType<decimal>(item.Row!.Values["value"].Value);
    }

    private static EtlJsonPageRecord ReadDecimalRecord(string rawValue) => Assert.Single(Read(
        "{\"records\":[{\"value\":" + rawValue + "}]}",
        Source([new EtlJsonField("value", "/value", EtlColumnType.Decimal, Required: true)])).Records);

    private static EtlHttpJsonSource Source(
        IReadOnlyList<EtlJsonField>? fields = null,
        EtlHttpPagination? pagination = null,
        string recordsPointer = "/records") => new(
        "source",
        recordsPointer,
        fields ?? [new EtlJsonField("id", "/id", EtlColumnType.Int64, Required: true), new EtlJsonField("name", "/name", EtlColumnType.String, Required: true)],
        pagination ?? new EtlTokenPagination("/next", "cursor"));

    private static (string Json, string Pointer) NestedPage(int containerDepth)
    {
        var json = "{\"records\":[]}";
        var pointer = "/records";
        // The base page has an object and records array; each wrapper adds one object level.
        for (var depth = 2; depth < containerDepth; depth++)
        {
            json = "{\"x\":" + json + "}";
            pointer = "/x" + pointer;
        }
        return (json, pointer);
    }
}
