using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Activities.Design.Tests.Api;

public sealed class InputDefaultRawJsonTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Activity_defaults_keep_raw_number_escape_and_whitespace_spelling(bool indented)
    {
        const string raw = """{ "OrderKey": "Ren\u00e9 <>&", "number": 1.0, "rows": [ null, 9007199254740993 ] }""";
        using var value = JsonDocument.Parse(raw);
        var input = new InputDefinition("OrderKey", "OrderKey", new TypeReference("Any"), null, "Order", null, true,
            DefaultValue: value.RootElement.Clone(), DefaultSyntax: "Literal");
        var options = JsonBaseline.CreateCopy(); options.WriteIndented = indented;
        var json = JsonSerializer.Serialize(input, options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(raw, document.RootElement.GetProperty("defaultValue").GetRawText());
        Assert.Equal(raw, JsonSerializer.Deserialize<InputDefinition>(json, options)!.DefaultValue!.Value.GetRawText());
    }
}
