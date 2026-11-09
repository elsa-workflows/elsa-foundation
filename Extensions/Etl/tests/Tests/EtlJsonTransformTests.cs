using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Csv;
using Elsa.Etl.Runtime.Http;
using Elsa.Etl.Runtime.Transformation;

namespace Elsa.Etl.Tests;

public sealed class EtlJsonTransformTests
{
    [Fact]
    public void Decoded_json_preserves_embedded_csv_characters_through_mapping()
    {
        var pipeline = EtlV2TestPipelines.Create();
        var page = Read("{\"records\":[{\"id\":1,\"name\":\"Ada, \\\"A\\\"\\nNext\"}]}", pipeline);
        var row = Assert.Single(page.Records);

        var result = new EtlRowTransformer(pipeline).Transform(row.Row!, 256);

        Assert.Null(row.Error);
        Assert.Equal(EtlRowDisposition.Accepted, result.Disposition);
        using var output = JsonDocument.Parse(result.JsonlBytes);
        Assert.Equal("Ada, \"A\"\nNext", output.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void Nonempty_decoded_page_can_filter_to_no_output_without_becoming_end_of_input()
    {
        var pipeline = EtlV2TestPipelines.Create() with
        {
            Filters = [new EtlFilter("name", EtlComparison.Equal, JsonSerializer.SerializeToElement("keep"))]
        };
        var page = Read("{\"records\":[{\"id\":1,\"name\":\"drop\"}],\"next\":\"page-2\"}", pipeline);
        var row = Assert.Single(page.Records);

        var result = new EtlRowTransformer(pipeline).Transform(row.Row!, 256);

        Assert.Null(row.Error);
        Assert.True(row.RawInputBytes > 0);
        Assert.Equal(EtlRowDisposition.Filtered, result.Disposition);
        Assert.True(result.JsonlBytes.IsEmpty);
        Assert.Null(result.Error);
        Assert.Equal("page-2", page.Continuation);
    }

    [Fact]
    public async Task Decoded_json_joins_a_real_csv_lookup_by_typed_key()
    {
        var pipeline = EtlV2TestPipelines.Create() with
        {
            Lookup = new EtlCsvLookup("lookup",
                [new EtlColumn("id", EtlColumnType.Int64, true), new EtlColumn("tier", EtlColumnType.String, true)],
                "id", "id"),
            Mapping = [new EtlOutputField("name", EtlFieldOrigin.Source, "name"), new EtlOutputField("tier", EtlFieldOrigin.Lookup, "tier")]
        };
        using var csv = new MemoryStream("tier,id\ngold,17\n"u8.ToArray());
        await using var records = new CsvRecordReader(csv, 256, 2).ReadAsync().GetAsyncEnumerator();
        Assert.True(await records.MoveNextAsync());
        Assert.True(EtlRowTransformer.TryCompileHeader(pipeline.Lookup.Schema, records.Current, out var header, out var headerError), headerError?.Code);
        Assert.True(await records.MoveNextAsync());
        Assert.True(EtlRowTransformer.TryParseTypedRow(pipeline.Lookup.Schema, header!, records.Current, out var lookupRow, out var rowError), rowError?.Code);
        Assert.False(await records.MoveNextAsync());
        var lookup = new Dictionary<EtlTypedValue, EtlTypedRow> { [lookupRow!.Values["id"]] = lookupRow };
        var page = Read("{\"records\":[{\"id\":17,\"name\":\"Ada\"}]}", pipeline);

        var result = new EtlRowTransformer(pipeline, lookup).Transform(Assert.Single(page.Records).Row!, 256);

        Assert.Equal(EtlRowDisposition.Accepted, result.Disposition);
        Assert.Equal("{\"name\":\"Ada\",\"tier\":\"gold\"}\n", Encoding.UTF8.GetString(result.JsonlBytes.Span));
    }

    private static EtlJsonPage Read(string json, EtlPipelineV2 pipeline) =>
        EtlJsonPageReader.Read(Encoding.UTF8.GetBytes(json), (EtlHttpJsonSource)pipeline.Source, pipeline.Limits, pipeline.Http);
}
