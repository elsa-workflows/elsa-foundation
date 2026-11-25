using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class NullableDefaultRawJsonTests
{
    private const string Raw = """{ "OrderKey": "Ren\u00e9 <>&", "number": 1.0, "rows": [ null, 9007199254740993 ] }""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Executable_defaults_keep_carried_text_including_with_an_indented_outer_writer(bool indented)
    {
        using var value = JsonDocument.Parse(Raw);
        var options = JsonBaseline.CreateCopy();
        options.WriteIndented = indented;
        var declared = new WorkflowDeclaredInput("OrderKey", new TypeReference("Any"), false, value.RootElement.Clone(), true);
        var declaredJson = JsonSerializer.Serialize(declared, options);
        using var declaredDocument = JsonDocument.Parse(declaredJson);
        Assert.Equal(Raw, declaredDocument.RootElement.GetProperty("defaultValue").GetRawText());
        Assert.Equal(Raw, JsonSerializer.Deserialize<WorkflowDeclaredInput>(declaredJson, options)!.DefaultValue!.Value.GetRawText());
    }
}
