using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// #5803: <see cref="WriteHttpResponse.Headers"/> accepts one string or an array of strings per header, as Elsa 3 did, at
/// the input boundary every run reads through.
/// </summary>
public sealed class WriteHttpResponseHeaderValuesTests
{
    private static readonly System.Reflection.PropertyInfo Headers = typeof(WriteHttpResponse).GetProperty(nameof(WriteHttpResponse.Headers))!;

    [Theory]
    [InlineData(typeof(IDictionary<string, string[]>))]
    [InlineData(typeof(Dictionary<string, string[]>))]
    [InlineData(typeof(IReadOnlyDictionary<string, string[]>))]
    public void A_single_string_header_reads_as_one_value(Type type)
    {
        var headers = Read("""{"X-Demo":"one"}""", type);

        Assert.Equal(["one"], headers["X-Demo"]);
    }

    [Fact]
    public void Array_and_single_string_headers_read_together()
    {
        var headers = Read("""{"X-One":"a","X-Many":["b","c"],"X-None":[]}""");

        Assert.Equal(["a"], headers["X-One"]);
        Assert.Equal(["b", "c"], headers["X-Many"]);
        Assert.Empty(headers["X-None"]);
    }

    [Fact]
    public void Header_names_are_kept_as_written()
    {
        var headers = Read("""{"x-Lower":"a","X-UPPER":"b"}""");

        Assert.Equal(["x-Lower", "X-UPPER"], headers.Keys);
    }

    [Theory]
    [InlineData("""{"X-Demo":1}""")]
    [InlineData("""{"X-Demo":[1]}""")]
    [InlineData("""{"X-Demo":{"a":"b"}}""")]
    [InlineData("""["X-Demo"]""")]
    [InlineData("\"X-Demo\"")]
    public void Values_that_are_neither_a_string_nor_an_array_of_strings_are_refused(string json) =>
        Assert.ThrowsAny<JsonException>(() => Read(json));

    [Fact]
    public void Single_string_headers_report_no_unmatched_members()
    {
        var warnings = new List<JsonConversionDiagnostic>();

        Read("""{"X-One":"a","X-Many":["b"]}""", warnings: warnings);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Headers_are_written_with_an_array_per_header()
    {
        var contract = JsonConversionContract.For(Headers.PropertyType, Headers);
        IDictionary<string, string[]> headers = new Dictionary<string, string[]> { ["X-Demo"] = ["one"] };

        var written = contract.Write(headers, Headers.PropertyType);

        Assert.Equal("""{"X-Demo":["one"]}""", written.GetRawText());
    }

    private static IReadOnlyDictionary<string, string[]> Read(string json, Type? type = null, List<JsonConversionDiagnostic>? warnings = null)
    {
        type ??= Headers.PropertyType;
        var value = JsonConversionContract.For(type, Headers).Read(JsonSerializer.Deserialize<JsonElement>(json), type, warnings is null ? null : warnings.Add);
        return Assert.IsAssignableFrom<IReadOnlyDictionary<string, string[]>>(value);
    }
}
