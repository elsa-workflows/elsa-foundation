using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnDurationBindingTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_timed_model_retains_its_explicit_binding_inventory()
    {
        var definition = JsonSerializer.Deserialize<BpmnModelDefinition>(
            """{"formatVersion":4,"process":{"processId":"timer"},"humanBindings":["review"],"timerBindings":["timeout"]}""", Web)!;
        var saved = JsonSerializer.SerializeToElement(definition, Web);
        Assert.Equal(4, definition.FormatVersion);
        Assert.Equal("timeout", Assert.Single(saved.GetProperty("timerBindings").EnumerateArray()).GetString());
        Assert.Equal(saved.GetRawText(), JsonSerializer.SerializeToElement(saved.Deserialize<BpmnModelDefinition>(Web), Web).GetRawText());
    }

    [Fact]
    public void Legacy_material_cannot_silently_ignore_a_timer_inventory() =>
        Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize<BpmnModelDefinition>(
            """{"formatVersion":1,"process":{"processId":"timer"},"humanBindings":["review"],"timerBindings":["timeout"]}""", Web));

    [Fact]
    public void Existing_human_material_has_no_new_timer_field()
    {
        const string original = """{"formatVersion":1,"process":{"processId":"human"},"humanBindings":["review"]}""";
        var definition = JsonSerializer.Deserialize<BpmnModelDefinition>(original, Web)!;
        Assert.Equal(original, JsonSerializer.Serialize(definition, Web));
    }
}
