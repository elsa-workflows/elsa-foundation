using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Spec 1228 FR-028: a node that uses an expression language with a registered value adapter is pinned to that adapter's
/// version, and the runtime refuses a node published with no pin or another version, asking to republish it.
/// </summary>
public sealed class ExecutableNodeValueAdaptersTests
{
    private static readonly ExpressionValueAdapter JavaScript = new("JavaScript", "json-1");
    private static readonly ScriptActivityLanguage RunJavaScript = new("Elsa.RunJavaScript", "JavaScript");

    [Fact]
    public void A_javascript_input_expression_or_a_script_activity_needs_the_pin_and_nothing_else_does()
    {
        Assert.Equal([JavaScript], ExecutableNodeValueAdapters.Required("Any", [Expression("JavaScript")], [JavaScript], []));
        Assert.Equal([JavaScript], ExecutableNodeValueAdapters.Required("Elsa.RunJavaScript", [], [JavaScript], [RunJavaScript]));
        Assert.Empty(ExecutableNodeValueAdapters.Required("Any", [Expression("Liquid")], [JavaScript], [RunJavaScript]));
        Assert.Empty(ExecutableNodeValueAdapters.Required("Any", [Expression("JavaScript")], [], []));
    }

    [Fact]
    public void A_tree_is_pinned_node_by_node()
    {
        var child = Node("child", "Elsa.RunJavaScript");
        var root = Node("root", "Sequence", children: [child]);

        var pinned = ExecutableNodeValueAdapters.PinTree(root, [JavaScript], [RunJavaScript]);

        Assert.False(pinned.Metadata.ContainsKey(ExecutableNodeValueAdapters.Key("JavaScript")));
        Assert.Equal("json-1", pinned.ChildSlots.Single().Activities.Single().Metadata[ExecutableNodeValueAdapters.Key("JavaScript")]);
        Assert.Same(root, ExecutableNodeValueAdapters.PinTree(root, [], [RunJavaScript]));
    }

    [Theory]
    [InlineData(null, "no JavaScript value adapter version")]
    [InlineData("json-0", "JavaScript value adapter version 'json-0'")]
    public void A_node_without_the_pin_or_with_another_version_is_refused(string? pin, string expected)
    {
        var metadata = pin is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [ExecutableNodeValueAdapters.Key("JavaScript")] = pin };
        var node = Node("script", "Elsa.RunJavaScript", metadata);

        var exception = Assert.Throws<InvalidOperationException>(() => ExecutableNodeValueAdapters.Ensure(node, [JavaScript], [RunJavaScript]));

        Assert.StartsWith(ExecutableNodeValueAdapters.MismatchCode, exception.Message, StringComparison.Ordinal);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        Assert.Contains("'script'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Republish the workflow", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pinned_node_runs()
    {
        var node = ExecutableNodeValueAdapters.PinTree(Node("script", "Elsa.RunJavaScript"), [JavaScript], [RunJavaScript]);

        ExecutableNodeValueAdapters.Ensure(node, [JavaScript], [RunJavaScript]);
    }

    private static RuntimeInputBinding Expression(string language) => new(
        "value",
        new ValueTypeDescriptor("String"),
        ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.Expression,
        expression: new RuntimeExpressionBinding(language, "1"));

    private static ExecutableNode Node(
        string id,
        string activityType,
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyCollection<ExecutableNode>? children = null) => new(
        executableNodeId: id,
        authoredActivityId: id,
        activityType: activityType,
        activityTypeVersion: "1.0.0",
        descriptorType: "test",
        descriptorPayload: JsonSerializer.SerializeToElement(new { }),
        inputBindings: new Dictionary<string, RuntimeInputBinding>(),
        metadata: metadata ?? new Dictionary<string, string>(),
        childSlots: children is null ? [] : [new ExecutableChildSlot("children", children.ToArray())]);
}
