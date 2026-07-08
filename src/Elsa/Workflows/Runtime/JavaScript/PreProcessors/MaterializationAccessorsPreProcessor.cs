using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Helpers;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Workflows.Primitives.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.JavaScript.PreProcessors;

/// <summary>
/// Surfaces the workflow variables, workflow inputs, and prior activity outputs carried by the
/// materialization-time expression context (<see cref="IMaterializationExpressionState"/>) to a JavaScript
/// activity input expression. Because activity inputs are materialized before the activity's real execution
/// context exists, the execution-time accessors (which read the live <see cref="IExecutionExpressionState"/>
/// carrier) are unavailable; this pre-processor reads the values straight off the materialization context instead.
/// </summary>
/// <remarks>
/// Registers the <c>variables</c>, <c>input</c>, and <c>output</c> container objects along with the
/// <c>getVariable</c>, <c>getInput</c>, <c>getOutput</c>, and <c>getOutputFrom</c> functions. It is a no-op
/// for any non-materialization evaluation, so it is safe to register globally.
/// </remarks>
public sealed class MaterializationAccessorsPreProcessor : IScriptPreProcessor
{
    public const string InputContainer = "input";
    public const string OutputContainer = "output";

    public ValueTask PreProcess(string script, IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken)
    {
        if (expressionContext is not IMaterializationExpressionState state)
            return ValueTask.CompletedTask;

        SetContainer(executionContext, VariableNames.VariableContainer, state.WorkflowVariables);
        SetContainer(executionContext, InputContainer, state.WorkflowInputs);
        SetContainer(executionContext, OutputContainer, state.ActivityOutputValues);

        executionContext.RegisterFunction(new JavaScriptFunction<string>(WorkflowFunctionNames.GetVariableFunctionName, name => GetValueOrNull(state.WorkflowVariables, name)));
        executionContext.RegisterFunction(new JavaScriptFunction<string>(WorkflowFunctionNames.GetInput, name => GetValueOrNull(state.WorkflowInputs, name)));
        executionContext.RegisterFunction(new JavaScriptFunction<string>("getOutput", name => GetValueOrNull(state.ActivityOutputValues, name)));
        executionContext.RegisterFunction(new JavaScriptFunction<string, string>(WorkflowFunctionNames.GetOutputFrom, (_, name) => GetValueOrNull(state.ActivityOutputValues, name)));

        return ValueTask.CompletedTask;
    }

    private static void SetContainer(IJavaScriptExecutionContext executionContext, string name, IReadOnlyDictionary<string, object?> values)
    {
        var container = new Dictionary<string, object?>();

        foreach (var (key, value) in values)
            container[key] = executionContext.NormalizeValue(value);

        executionContext.SetValue(name, executionContext.NormalizeValue(container));
    }

    // Lift the value to the canonical JsonNode (ADR 0036) so the getVariable/getInput/getOutput functions
    // return the same representation as the variables/input/output containers.
    private static object? GetValueOrNull(IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) ? DynamicValueMaterializer.Materialize(value) : null;
}
