using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Csv;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Transformation;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1191: RFC 4180 formatting of CSV route bodies.</summary>
public sealed class EtlCsvRouteWriterTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line\nbreak", "\"line\nbreak\"")]
    [InlineData("carriage\rreturn", "\"carriage\rreturn\"")]
    [InlineData(" leading", "\" leading\"")]
    [InlineData("trailing\t", "\"trailing\t\"")]
    [InlineData(" nbsp", "\" nbsp\"")]
    [InlineData("in side", "in side")]
    [InlineData("=SUM(A1)", "=SUM(A1)")]
    [InlineData("Zoë 東京", "Zoë 東京")]
    public void Strings_are_quoted_only_when_they_need_it(string value, string expected) =>
        Assert.Equal(expected, EtlCsvRouteWriter.Text(value));

    [Fact]
    public void Record_rows_keep_mapping_order_and_exact_scalar_text()
    {
        var json = "{\"name\":\"a,b\",\"count\":-9223372036854775808,\"amount\":1.50,\"active\":true,\"note\":null,\"empty\":\"\"}\n";

        var row = Encoding.UTF8.GetString(EtlCsvRouteWriter.RecordRow(Encoding.UTF8.GetBytes(json), 6));

        Assert.Equal("\"a,b\",-9223372036854775808,1.50,true,,\"\"\r\n", row);
    }

    [Fact]
    public void Headers_and_error_rows_use_the_string_rule()
    {
        Assert.Equal("id,\"a,b\",\"say \"\"x\"\"\"\r\n", Encoding.UTF8.GetString(EtlCsvRouteWriter.Header(["id", "a,b", "say \"x\""])));
        Assert.Equal("recordOrdinal,code,field\r\n", Encoding.UTF8.GetString(EtlCsvRouteWriter.ErrorHeader()));
        Assert.Equal("7,etl.invalid-value,\"a,b\"\r\n", Encoding.UTF8.GetString(EtlCsvRouteWriter.ErrorRow(new EtlRowError(7, "a,b", "etl.invalid-value"))));
        Assert.Equal("3,etl.jsonl.record-invalid,\r\n", Encoding.UTF8.GetString(EtlCsvRouteWriter.ErrorRow(new EtlRowError(3, null, "etl.jsonl.record-invalid"))));
    }

    [Fact]
    public void A_single_null_column_row_is_an_empty_line()
    {
        Assert.Equal("\r\n", Encoding.UTF8.GetString(EtlCsvRouteWriter.RecordRow(Encoding.UTF8.GetBytes("{\"note\":null}"), 1)));
    }

    [Theory]
    [InlineData("{\"a\":1}", 2)]
    [InlineData("{\"a\":[1]}", 1)]
    [InlineData("{\"a\":{\"b\":1}}", 1)]
    [InlineData("[1]", 1)]
    public void Rows_that_do_not_match_their_mapping_are_refused(string json, int fieldCount) =>
        Assert.Throws<InvalidDataException>(() => EtlCsvRouteWriter.RecordRow(Encoding.UTF8.GetBytes(json), fieldCount));

    [Fact]
    public async Task Output_reads_back_through_the_csv_source_reader_with_its_typed_values()
    {
        var schema = new[]
        {
            new EtlColumn("name", EtlColumnType.String, false), new EtlColumn("count", EtlColumnType.Int64, false),
            new EtlColumn("amount", EtlColumnType.Decimal, false), new EtlColumn("active", EtlColumnType.Boolean, false)
        };
        var rows = new[]
        {
            "{\"name\":\"line\\nbreak, \\\"quoted\\\" \",\"count\":42,\"amount\":-0.0100,\"active\":false}",
            "{\"name\":\" Zoë 東京\",\"count\":null,\"amount\":79228162514264337593543950335,\"active\":true}"
        };
        var bytes = EtlCsvRouteWriter.Header(schema.Select(column => column.Name).ToArray())
            .Concat(rows.SelectMany(row => EtlCsvRouteWriter.RecordRow(Encoding.UTF8.GetBytes(row), schema.Length))).ToArray();

        var records = new List<CsvRecord>();
        await foreach (var record in new CsvRecordReader(new MemoryStream(bytes), 64 * 1024, 16).ReadAsync(CancellationToken.None))
            records.Add(record);
        Assert.True(EtlRowTransformer.TryCompileHeader(schema, records[0], out var header, out var headerError), headerError?.Code);
        var typed = records.Skip(1).Select(record =>
        {
            Assert.True(EtlRowTransformer.TryParseTypedRow(schema, header!, record, out var row, out var error), error?.Code);
            return row!.Values;
        }).ToArray();

        Assert.Equal("line\nbreak, \"quoted\" ", typed[0]["name"].Value);
        Assert.Equal(42L, typed[0]["count"].Value);
        Assert.Equal(-0.0100m, typed[0]["amount"].Value);
        Assert.Equal("-0.0100", ((decimal)typed[0]["amount"].Value!).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(false, typed[0]["active"].Value);
        Assert.Equal(" Zoë 東京", typed[1]["name"].Value);
        Assert.Null(typed[1]["count"].Value);
        Assert.Equal(decimal.MaxValue, typed[1]["amount"].Value);
        Assert.Equal(true, typed[1]["active"].Value);
    }
}
