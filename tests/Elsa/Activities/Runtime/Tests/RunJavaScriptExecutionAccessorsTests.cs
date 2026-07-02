using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Execution-time expression surfaces restored by ADR 0030: named pascalized variable accessors and JavaScript
/// variable write-back, evaluated through the real JavaScript pipeline (the whole
/// <c>JavaScriptWorkflowsRuntimeFeature</c> processor set) against the live execution-time carrier
/// (<c>SimpleActivityExecutionContext</c> implementing <c>IExecutionExpressionState</c>). Variable mutations must
/// land in the visible <see cref="VariableScope"/> — the same scope the scheduler folds into the checkpoint-commit
/// durable-value write-back — so the durable-persistence path (already covered by the write-back suite) carries
/// them forward. The final test guards coexistence: enabling the whole feature must not break materialization-time
/// evaluation (the execution-time processors no-op on the materialization carrier).
/// </summary>
[Collection("ConsoleCapture")]
public sealed class RunJavaScriptExecutionAccessorsTests
{
    private const string StringTypeName = "System.String";

    private static readonly WorkflowExecutableIdentity PinnedExecutable =
        new("artifact-1", "definition-1", "version-7", "7.0.0", "hash-1");

    [Fact]
    public async Task NamedVariableAccessor_ReadsCurrentScopeValue()
    {
        using var provider = JavaScriptRuntimeFeatureEvaluationTests.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IJavaScriptEvaluator>();
        var variableScope = NewWorkflowScope(("greeting", "Hello"));
        var context = NewContext(scope.ServiceProvider, variableScope);

        var result = await evaluator.EvaluateAsync("getGreeting()", typeof(object), context);

        Assert.Equal("Hello", result);
    }

    [Fact]
    public async Task VariableWriteBack_ThroughSetVariableAndNamedSetter_LandsInWorkflowScope()
    {
        using var provider = JavaScriptRuntimeFeatureEvaluationTests.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IJavaScriptEvaluator>();
        var variableScope = NewWorkflowScope(("greeting", "Hello"), ("status", "pending"));
        var context = NewContext(scope.ServiceProvider, variableScope);

        await evaluator.EvaluateAsync("setVariable('greeting', 'Hi'); setStatus('approved'); 1", typeof(object), context);

        Assert.True(variableScope.TryGetValueByName("greeting", out var greeting));
        Assert.Equal("Hi", greeting);
        Assert.True(variableScope.TryGetValueByName("status", out var status));
        Assert.Equal("approved", status);
    }

    [Fact]
    public async Task VariableWriteBack_ThroughDirectContainerAssignment_LandsInWorkflowScope()
    {
        using var provider = JavaScriptRuntimeFeatureEvaluationTests.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IJavaScriptEvaluator>();
        var variableScope = NewWorkflowScope(("greeting", "Hello"));
        var context = NewContext(scope.ServiceProvider, variableScope);

        await evaluator.EvaluateAsync("variables.greeting = 'Direct'; 1", typeof(object), context);

        Assert.True(variableScope.TryGetValueByName("greeting", out var greeting));
        Assert.Equal("Direct", greeting);
    }

    [Fact]
    public async Task FullFeatureEnabled_MaterializationTimeEvaluationStillResolves()
    {
        // Coexistence guard (SC-006): with the whole JavaScript runtime feature enabled, the execution-time
        // processors must no-op on the materialization carrier so input-materialization evaluation is unaffected.
        using var provider = JavaScriptRuntimeFeatureEvaluationTests.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        var resolutionContext = new RuntimeInputBindingResolutionContext(
            workflowExecutionId: "wfexec-1",
            activityExecutionId: "actexec-1",
            durableValuesByValueId: new Dictionary<string, DurableValueState>(),
            activityOutputs: EmptyActivityOutputReader.Instance,
            serviceProvider: serviceProvider,
            workflowVariables: new Dictionary<string, object?> { ["greeting"] = "Hello" },
            workflowInputs: new Dictionary<string, object?> { ["name"] = "World" },
            activityOutputValues: new Dictionary<string, object?>());

        var node = NewExpressionNode("materialize-js", "variables.greeting + \" \" + input.name");
        var materializer = new RuntimeActivityInputMaterializer();

        var materialized = await materializer.MaterializeInputsAsync(node, resolutionContext);

        Assert.Equal("Hello World", Assert.Single(materialized).Value);
    }

    private static SimpleActivityExecutionContext NewContext(IServiceProvider serviceProvider, VariableScope variableScope) =>
        new(
            serviceProvider,
            new TestActivity(),
            CancellationToken.None,
            workflowExecutionId: "wfexec-1",
            pinnedExecutable: PinnedExecutable,
            variableScope: variableScope);

    private static VariableScope NewWorkflowScope(params (string Name, object? Value)[] variables)
    {
        var variablesByReferenceKey = variables.ToDictionary(
            entry => entry.Name,
            entry => (IVariable)new Variable(entry.Name, entry.Value),
            StringComparer.Ordinal);
        var initialValues = variables.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
        return new VariableScope("workflow", variablesByReferenceKey, initialValues: initialValues);
    }

    private static ExecutableNode NewExpressionNode(string nodeId, string expression) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "Test.Expression",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Value"] = new(
                    inputName: "Value",
                    source: RuntimeInputBindingSource.Expression,
                    expression: new RuntimeExpressionBinding("JavaScript", expression, new RuntimeValueTypeDescriptor("clr", StringTypeName, null)),
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = StringTypeName,
                        ["referenceKey"] = "Value"
                    })
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private sealed class EmptyActivityOutputReader : IRuntimeActivityOutputReader
    {
        public static readonly EmptyActivityOutputReader Instance = new();

        public bool TryGet(ActiveActivityOutputKey key, out ActiveActivityOutput output)
        {
            output = null!;
            return false;
        }

        public IReadOnlyCollection<ActiveActivityOutput> GetActivityOutputs(string workflowExecutionId, string activityExecutionId) => [];
    }

    private sealed class TestActivity : IActivity
    {
        public string Id { get; set; } = "activity-1";
        public string NodeId { get; set; } = "node-1";
        public string? Name { get; set; }
        public string Type { get; set; } = "Test.Activity";
        public string Version { get; set; } = "1.0.0";
        public Dictionary<string, object> CustomProperties { get; set; } = new();
        public Dictionary<string, object> SyntheticProperties { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
        public ValueTask<bool> CanExecuteAsync(IActivityExecutionContext context) => new(true);
        public ValueTask ExecuteAsync(IActivityExecutionContext context) => ValueTask.CompletedTask;
    }
}
