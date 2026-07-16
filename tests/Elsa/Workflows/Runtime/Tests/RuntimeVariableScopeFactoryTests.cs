using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeVariableScopeFactoryTests
{
    private readonly RuntimeVariableDeclarationProjector _projector = new();

    [Fact]
    public void Declarations_and_frame_values_are_keyed_by_stable_reference_key()
    {
        var node = Node(("var-counter", "Counter", 7));

        var declarations = _projector.ProjectDeclarations(node);
        var values = _projector.ProjectInitialValues(node);

        Assert.Equal("Counter", declarations["var-counter"].Name);
        Assert.DoesNotContain("Counter", values.Keys);
        Assert.Equal(7, values["var-counter"].InlineValue!.Value.GetInt32());
    }

    [Fact]
    public void Missing_default_is_an_explicit_absent_envelope()
    {
        var node = Node(("var-value", "Value", null));

        Assert.Equal(ValuePresence.ExplicitNull, _projector.ProjectInitialValues(node)["var-value"].Presence);
    }

    internal static ExecutableNode Node(params (string Key, string Name, object? Default)[] variables) =>
        new(
            executableNodeId: "container",
            authoredActivityId: "authored-container",
            activityType: "test/container",
            activityTypeVersion: "1.0.0",
            descriptorType: "test/descriptor",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            structure: new ExecutableActivityStructure(
                "test.structure",
                "1.0.0",
                JsonSerializer.SerializeToElement(new
                {
                    variables = variables.Select(item => Declaration(item.Key, item.Name, item.Default))
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    private static RuntimeVariableDeclaration Declaration(string key, string name, object? value)
    {
        var type = new ValueTypeDescriptor("Int32");
        var policy = ValueProtectionPolicy.InstanceInline;
        var envelope = value is null
            ? ValueEnvelope.Null(type, policy)
            : ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), policy);
        return new RuntimeVariableDeclaration(
            key,
            name,
            type,
            policy,
            new RuntimeInputBinding(key, type, policy, RuntimeInputBindingSource.Literal, literal: envelope));
    }
}
