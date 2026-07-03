using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Primitives.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.JavaScript.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.JavaScript.PreProcessors;

/// <summary>
/// Registers the generic <c>getVariable</c>/<c>setVariable</c> functions plus named pascalized variable
/// accessors (e.g. <c>getGreeting()</c>/<c>setGreeting()</c> for a variable named <c>greeting</c>) from the
/// live execution-time expression carrier (<see cref="IExecutionExpressionState"/>). Re-pointed onto the carrier
/// per ADR 0030: no DI-registered live workflow execution context. Reads and writes resolve through the visible
/// container-scope chain (<see cref="IScopedVariableProvider"/>, ADR 0027) with nearest-scope shadowing, so
/// mutations land in the workflow/container scope and fold into the checkpoint-commit durable-value write-back.
/// No-op for any non-execution context (e.g. input materialization), so it is safe to register globally.
/// </summary>
public sealed class VariableFunctionsPreProcessor(IOptions<FeatureOptions> options)
    : IScriptPreProcessor
{
    public ValueTask PreProcess(string script, IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? evaluatorOptions, CancellationToken cancellationToken)
    {
        if (expressionContext is not IExecutionExpressionState state)
            return ValueTask.CompletedTask;

        // When the activity's expression context exposes a visible scope chain (workflow + ancestor
        // container scopes, ADR 0027), resolve name-based helpers through it so freehand scripts can
        // read/assign container-scoped variables with nearest-scope shadowing. Otherwise fall back to
        // the workflow-scope variable projection carried by the execution-time carrier.
        var scopedVariables = expressionContext as IScopedVariableProvider;

        var variableNames = scopedVariables is not null
            ? scopedVariables.GetVisibleVariables().Select(variable => variable.Name)
            : state.WorkflowVariables.Keys;

        foreach (var variableName in variableNames)
            foreach (var function in BuildNamedVariableFunctions(executionContext, scopedVariables, state, variableName))
                executionContext.RegisterFunction(function);

        executionContext.RegisterFunction(
            new JavaScriptFunction<string, object>(
                WorkflowFunctionNames.SetVariableFunctionName,
                (name, value) => SetVariable(executionContext, scopedVariables, name, value)
            )
        );

        executionContext.RegisterFunction(
            new JavaScriptFunction<string>(
                WorkflowFunctionNames.GetVariableFunctionName,
                (name) => GetVariable(scopedVariables, state, name)
            )
        );

        return ValueTask.CompletedTask;
    }

    private IEnumerable<IJavaScriptFunction> BuildNamedVariableFunctions(IJavaScriptExecutionContext context, IScopedVariableProvider? scopedVariables, IExecutionExpressionState state, string variableName)
    {
        var pascalName = variableName.Pascalize();

        yield return new JavaScriptFunction<object>(
            string.Format(WorkflowFunctionNames.SetNamedVariableFunctionFormat, pascalName),
            (value) => SetVariable(context, scopedVariables, variableName, value));

        yield return new JavaScriptFunction(
            string.Format(WorkflowFunctionNames.GetNamedVariableFunctionFormat, pascalName),
            () => GetVariable(scopedVariables, state, variableName));
    }

    private static object? GetVariable(IScopedVariableProvider? scopedVariables, IExecutionExpressionState state, string name)
    {
        if (scopedVariables is not null && scopedVariables.TryGetVariableValueByName(name, out var value))
            return value;

        return state.WorkflowVariables.GetValueOrDefault(name);
    }

    private void SetVariable(IJavaScriptExecutionContext context, IScopedVariableProvider? scopedVariables, string name, object? value)
    {
        if (options.Value.DisableVariableCopying)
            return;

        // To ensure both variable accessor syntaxes work, we need to update the variables container in the engine as well as the context
        // to keep them in sync.

        // Variables Container
        var variablesContainer = (IDictionary<string, object?>?)context.GetValue(
            VariableNames.VariableContainer
        );
        variablesContainer ??= new Dictionary<string, object?>();

        // Set value in JavaScript Execution Context
        variablesContainer[name] = context.NormalizeValue(value);
        context.SetValue(
            VariableNames.VariableContainer,
            variablesContainer
        );

        // Write back to the correct visible scope (nearest workflow/container scope declaring the name) when a
        // scope chain is available, so the mutation folds into the checkpoint-commit durable-value write-back.
        // A name declared by no scope has no durable target under the artifact-only runtime model; it remains
        // readable within this evaluation via the engine container above.
        scopedVariables?.TrySetVariableValueByName(name, value);
    }
}
