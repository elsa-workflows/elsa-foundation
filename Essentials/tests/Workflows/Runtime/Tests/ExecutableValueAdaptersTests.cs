using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Spec 1228 FR-028: an executable that uses an expression language with a registered value adapter carries one pin per
/// language in its hashed content, and the runtime refuses one published with no pin or another version.
/// </summary>
public sealed class ExecutableValueAdaptersTests
{
    private static readonly ExpressionValueAdapter JavaScript = new("JavaScript", "json-1");
    private static readonly ScriptActivityLanguage RunJavaScript = new("Elsa.RunJavaScript", "JavaScript");

    [Fact]
    public void An_input_expression_a_variable_initial_binding_or_a_script_activity_needs_the_pin_and_nothing_else_does()
    {
        var pin = new Dictionary<string, string> { ["javascript.valueAdapter"] = "json-1" };
        Assert.Equal(pin, ExecutableValueAdapters.Required([Node("a", "Any", Expression("JavaScript"))], null, [JavaScript], []));
        Assert.Equal(pin, ExecutableValueAdapters.Required([Node("a", "Elsa.RunJavaScript")], null, [JavaScript], [RunJavaScript]));
        Assert.Equal(pin, ExecutableValueAdapters.Required([Node("a", "Any")], [Variable(Expression("javascript"))], [JavaScript], []));
        Assert.Empty(ExecutableValueAdapters.Required([Node("a", "Any", Expression("Liquid"))], null, [JavaScript], [RunJavaScript]));
        Assert.Empty(ExecutableValueAdapters.Required([Node("a", "Any", Expression("JavaScript"))], null, [], []));
    }

    [Theory]
    [InlineData(null, "no JavaScript value adapter version")]
    [InlineData("json-0", "JavaScript value adapter version 'json-0'")]
    public void An_executable_without_the_pin_or_with_another_version_is_refused(string? pin, string expected)
    {
        var metadata = pin is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["javascript.valueAdapter"] = pin };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ExecutableValueAdapters.Ensure(Executable(metadata), [JavaScript], [RunJavaScript]));

        Assert.StartsWith(ExecutableValueAdapters.MismatchCode, exception.Message, StringComparison.Ordinal);
        Assert.Contains(expected.Replace("JavaScript", "javascript", StringComparison.Ordinal), exception.Message, StringComparison.Ordinal);
        Assert.Contains("Republish the workflow", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pinned_executable_runs_and_a_host_without_the_adapter_checks_nothing()
    {
        ExecutableValueAdapters.Ensure(Executable(new Dictionary<string, string> { ["javascript.valueAdapter"] = "json-1" }), [JavaScript], [RunJavaScript]);
        ExecutableValueAdapters.Ensure(Executable(new Dictionary<string, string>()), [], []);
    }

    [Fact]
    public void The_pin_is_in_the_hash_and_an_executable_without_one_keeps_its_hash()
    {
        var hasher = new WorkflowExecutableHasher();
        var root = Node("script", "Elsa.RunJavaScript");
        var contract = new WorkflowExecutableInputContract(1, []);

        var plain = hasher.ComputeHash(root, contract, []);
        Assert.Equal(plain, hasher.ComputeHash(root, contract, [], valueAdapters: new Dictionary<string, string>()));
        var pinned = hasher.ComputeHash(root, contract, [], valueAdapters: new Dictionary<string, string> { ["javascript.valueAdapter"] = "json-1" });
        Assert.NotEqual(plain, pinned);
        Assert.NotEqual(pinned, hasher.ComputeHash(root, contract, [], valueAdapters: new Dictionary<string, string> { ["javascript.valueAdapter"] = "json-2" }));
    }

    private static WorkflowExecutable Executable(IReadOnlyDictionary<string, string> metadata) => new(
        new WorkflowExecutableIdentity("artifact-js", "sha256:00", "definition", "version", "1.0.0"),
        Node("script", "Elsa.RunJavaScript"),
        new Dictionary<string, WorkflowExecutableResumeTarget>(),
        DateTimeOffset.UnixEpoch,
        metadata,
        IncidentStrategyBuiltIns.FaultReference);

    private static RuntimeVariableDeclaration Variable(RuntimeInputBinding binding) =>
        new("var", "var", new ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline, binding);

    private static RuntimeInputBinding Expression(string language) => new(
        "value",
        new ValueTypeDescriptor("String"),
        ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.Expression,
        expression: new RuntimeExpressionBinding(language, "1"));

    private static ExecutableNode Node(string id, string activityType, RuntimeInputBinding? binding = null) => new(
        executableNodeId: id,
        authoredActivityId: id,
        activityType: activityType,
        activityTypeVersion: "1.0.0",
        descriptorType: "test",
        descriptorPayload: JsonSerializer.SerializeToElement(new { }),
        inputBindings: binding is null ? new Dictionary<string, RuntimeInputBinding>() : new Dictionary<string, RuntimeInputBinding> { ["value"] = binding },
        metadata: new Dictionary<string, string>());
}
