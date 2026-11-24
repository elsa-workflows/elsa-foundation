using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

public sealed class RecursiveJsonConversionContractTests
{
    [Theory]
    [InlineData("Child")]
    [InlineData("ArrayChildren")]
    [InlineData("ListChildren")]
    [InlineData("NamedChildren")]
    public void Recursive_type_selection_reads_and_writes_below_a_member_override(string shape)
    {
        var contract = InputContract<StrictNode>(nameof(Inputs.Strict));
        var input = Input(shape, """{"customer_name":"Child","child":{"customer_name":"Grandchild"}}""");
        var original = input.GetRawText();
        var diagnostics = new List<JsonConversionDiagnostic>();
        Assert.Empty(UnmatchedMemberScanner.Scan(input, typeof(StrictNode), contract));

        var value = (StrictNode)contract.Read(input, typeof(StrictNode), diagnostics.Add)!;
        Assert.Equal("Root", value.CustomerName);
        Assert.Equal("Child", Nested(value, shape).CustomerName);
        Assert.Equal("Grandchild", Nested(value, shape).Child!.CustomerName);
        Assert.Empty(diagnostics);
        Assert.Equal(original, input.GetRawText());

        var written = contract.Write(value, typeof(StrictNode));
        Assert.Equal("Root", written.GetProperty("CustomerName").GetString());
        var nested = Nested(written, shape);
        Assert.Equal("Child", nested.GetProperty("customer_name").GetString());
        Assert.False(nested.TryGetProperty("CustomerName", out _));
        Assert.Equal("Grandchild", nested.GetProperty("child").GetProperty("customer_name").GetString());
        Assert.Empty(UnmatchedMemberScanner.Scan(written, typeof(StrictNode), contract));
        var reread = (StrictNode)contract.Read(written, typeof(StrictNode), diagnostics.Add)!;
        Assert.Equal("Child", Nested(reread, shape).CustomerName);
        Assert.Equal("Grandchild", Nested(reread, shape).Child!.CustomerName);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("Child", "$[\"Child\"][\"unexpected\"]")]
    [InlineData("ArrayChildren", "$[\"ArrayChildren\"][0][\"unexpected\"]")]
    [InlineData("ListChildren", "$[\"ListChildren\"][0][\"unexpected\"]")]
    [InlineData("NamedChildren", "$[\"NamedChildren\"][\"KeepKey\"][\"unexpected\"]")]
    public void Recursive_strict_type_refusals_match_the_scanner_and_keep_nested_paths(string shape, string expectedPath)
    {
        var contract = InputContract<StrictNode>(nameof(Inputs.Strict));
        var input = Input(shape, """{"customer_name":"Child","unexpected":null}""");
        var scanned = Assert.Throws<JsonConversionException>(() => UnmatchedMemberScanner.Scan(input, typeof(StrictNode), contract));
        var converted = Assert.Throws<JsonConversionException>(() => contract.Read(input, typeof(StrictNode)));
        Assert.Equal("json-unmatched-member-refused", scanned.Code);
        Assert.Equal(expectedPath, scanned.Path);
        Assert.Equal(scanned.Code, converted.Code);
        Assert.Equal(scanned.Path, converted.Path);
    }

    [Theory]
    [InlineData("Child", "$[\"Child\"][\"unexpected\"]")]
    [InlineData("ArrayChildren", "$[\"ArrayChildren\"][0][\"unexpected\"]")]
    [InlineData("ListChildren", "$[\"ListChildren\"][0][\"unexpected\"]")]
    [InlineData("NamedChildren", "$[\"NamedChildren\"][\"KeepKey\"][\"unexpected\"]")]
    public void Recursive_type_warnings_override_a_strict_input_member_and_match_the_reader(string shape, string expectedPath)
    {
        var contract = InputContract<WarningNode>(nameof(Inputs.Warning));
        var input = Input(shape, """{"customer_name":"Child","unexpected":null}""");
        var scanned = Assert.Single(UnmatchedMemberScanner.Scan(input, typeof(WarningNode), contract));
        var diagnostics = new List<JsonConversionDiagnostic>();
        var value = (WarningNode)contract.Read(input, typeof(WarningNode), diagnostics.Add)!;
        Assert.Equal("Child", Nested(value, shape).CustomerName);
        var recorded = Assert.Single(diagnostics);
        Assert.Equal(scanned, recorded);
        Assert.Equal("json-unmatched-member", recorded.Code);
        Assert.Equal(expectedPath, recorded.Path);
        Assert.Equal(typeof(WarningNode), recorded.TargetType);

        var rootRefusal = Assert.Throws<JsonConversionException>(() => contract.Read(Element("""{"customer_name":"Wrong root casing"}"""), typeof(WarningNode)));
        Assert.Equal("json-unmatched-member-refused", rootRefusal.Code);
        Assert.Equal("$[\"customer_name\"]", rootRefusal.Path);
    }

    [Fact]
    public void Recursive_type_selection_without_a_member_override_keeps_its_round_trip()
    {
        var contract = JsonConversionContract.For(typeof(StrictNode));
        var input = Element("""{"customer_name":"Root","child":{"customer_name":"Direct"},"array_children":[{"customer_name":"Array"}],"list_children":[{"customer_name":"List"}],"named_children":{"KeepKey":{"customer_name":"Dictionary"}}}""");
        var value = (StrictNode)contract.Read(input, typeof(StrictNode))!;
        var reread = (StrictNode)contract.Read(contract.Write(value, typeof(StrictNode)), typeof(StrictNode))!;
        Assert.Equal("Root", reread.CustomerName);
        Assert.Equal("Direct", reread.Child!.CustomerName);
        Assert.Equal("Array", Assert.Single(reread.ArrayChildren!).CustomerName);
        Assert.Equal("List", Assert.Single(reread.ListChildren!).CustomerName);
        Assert.Equal("Dictionary", reread.NamedChildren!["KeepKey"].CustomerName);
    }

    private static JsonConversionContract InputContract<T>(string member) =>
        JsonConversionContract.For(typeof(T), typeof(Inputs).GetProperty(member)!);

    private static JsonElement Input(string shape, string child)
    {
        var contents = shape switch
        {
            "Child" => child,
            "ArrayChildren" or "ListChildren" => "[" + child + "]",
            "NamedChildren" => "{\"KeepKey\":" + child + "}",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        return Element("{\"CustomerName\":\"Root\",\"" + shape + "\":" + contents + "}");
    }

    private static T Nested<T>(RecursiveNode<T> node, string shape) where T : RecursiveNode<T> => shape switch
    {
        "Child" => node.Child!,
        "ArrayChildren" => Assert.Single(node.ArrayChildren!),
        "ListChildren" => Assert.Single(node.ListChildren!),
        "NamedChildren" => node.NamedChildren!["KeepKey"],
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    private static JsonElement Nested(JsonElement value, string shape)
    {
        var member = value.GetProperty(shape);
        return shape switch
        {
            "Child" => member,
            "ArrayChildren" or "ListChildren" => member[0],
            "NamedChildren" => member.GetProperty("KeepKey"),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
    }

    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class Inputs
    {
        [JsonConversion(NamingPolicy = JsonKnownNamingPolicy.Unspecified)] public StrictNode? Strict { get; set; }
        [JsonConversion(NamingPolicy = JsonKnownNamingPolicy.Unspecified, RefuseUnmatchedMembers = true)] public WarningNode? Warning { get; set; }
    }

    private abstract class RecursiveNode<T> where T : RecursiveNode<T>
    {
        public string? CustomerName { get; set; }
        public T? Child { get; set; }
        public T[]? ArrayChildren { get; set; }
        public List<T>? ListChildren { get; set; }
        public Dictionary<string, T>? NamedChildren { get; set; }
    }

    [JsonConversion(NamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, RefuseUnmatchedMembers = true)]
    private sealed class StrictNode : RecursiveNode<StrictNode> { }
    [JsonConversion(NamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
    private sealed class WarningNode : RecursiveNode<WarningNode> { }
}
