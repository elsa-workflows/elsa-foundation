using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Models;
using Elsa.Expressions.Options;
using Elsa.Expressions.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Sequence.Tests;

/// <summary>
/// Regression coverage for the non-string scoped-variable value path. Container variable values
/// round-trip through the container execution's persisted snapshot as JSON, so a non-string variable
/// (e.g. an <c>int</c>) reaches input materialization as a <see cref="JsonElement"/>. The materializer
/// must coerce it to the input's declared type, exactly as it does for literal bindings — otherwise
/// the activity receives a boxed element instead of the CLR value.
/// </summary>
public sealed class ScopedVariableInputCoercionTests
{
    private const string ScopeId = "container-node";
    private const string ReferenceKey = "var-counter";

    [Fact]
    public async Task Non_string_scoped_variable_is_coerced_to_the_declared_input_type()
    {
        await using var provider = ExpressionProvider();

        // Simulate a value reloaded from the container execution's snapshot: a JSON number, not a CLR int.
        var scope = new VariableScope(
            ScopeId,
            new Dictionary<string, IVariable> { [ReferenceKey] = new Variable("Counter", 0) });
        scope.TrySetValue(new VariableReference(ReferenceKey, ScopeId), JsonSerializer.Deserialize<JsonElement>("42"));

        var node = NodeWithVariableInput("System.Int32");
        var context = ResolutionContext(provider, scope);

        var materialized = await new RuntimeActivityInputMaterializer().MaterializeInputsAsync(node, context);

        var input = Assert.Single(materialized);
        Assert.IsType<int>(input.Value);
        Assert.Equal(42, input.Value);
    }

    private static ServiceProvider ExpressionProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExpressionDescriptorProvider, DefaultExpressionDescriptorProvider>();
        services.AddSingleton<IExpressionDescriptorRegistry>(sp => new ExpressionDescriptorRegistry(sp.GetServices<IExpressionDescriptorProvider>()));
        services.AddSingleton(Options.Create(ExpressionEvaluatorOptions.Empty));
        services.AddScoped<IExpressionEvaluator, ExpressionEvaluator>();
        return services.BuildServiceProvider();
    }

    private static ExecutableNode NodeWithVariableInput(string typeName) =>
        new(
            executableNodeId: "node-read",
            authoredActivityId: "authored-node-read",
            activityType: "test/read",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Value"] = new(
                    inputName: "Value",
                    source: RuntimeInputBindingSource.Expression,
                    expression: new RuntimeExpressionBinding(
                        "Variable",
                        JsonSerializer.Serialize(new { referenceKey = ReferenceKey, declaringScopeId = ScopeId }),
                        new RuntimeValueTypeDescriptor("clr", typeName, null)),
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = typeName,
                        ["referenceKey"] = "Value"
                    })
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static RuntimeInputBindingResolutionContext ResolutionContext(IServiceProvider serviceProvider, VariableScope scope) =>
        new(
            workflowExecutionId: "wfexec-1",
            activityExecutionId: "actexec-read",
            durableValuesByValueId: new Dictionary<string, DurableValueState>(),
            activityOutputs: NullOutputReader.Instance,
            serviceProvider: serviceProvider,
            variableScope: scope);

    private sealed class NullOutputReader : IRuntimeActivityOutputReader
    {
        public static readonly NullOutputReader Instance = new();
        public bool TryGet(ActiveActivityOutputKey key, out ActiveActivityOutput output) { output = null!; return false; }
        public IReadOnlyCollection<ActiveActivityOutput> GetActivityOutputs(string workflowExecutionId, string activityExecutionId) => [];
    }
}
