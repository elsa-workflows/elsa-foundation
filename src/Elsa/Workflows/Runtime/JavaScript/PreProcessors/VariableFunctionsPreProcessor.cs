using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Primitives.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.JavaScript.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.JavaScript.PreProcessors;

public sealed class VariableFunctionsPreProcessor(IOptions<FeatureOptions> options, IWorkflowExecutionContext workflowExecution)
    : IScriptPreProcessor
{
    public ValueTask PreProcess(string script, IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? evaluatorOptions, CancellationToken cancellationToken)
    {
        // When the activity's expression context exposes a visible scope chain (workflow + ancestor
        // container scopes, ADR 0027), resolve name-based helpers through it so freehand scripts can
        // read/assign container-scoped variables with nearest-scope shadowing. Otherwise fall back to
        // the workflow-scoped behaviour.
        var scopedVariables = expressionContext as IScopedVariableProvider;

        var namedVariables = scopedVariables is not null
            ? scopedVariables.GetVisibleVariables()
            : workflowExecution.GetVariables();

        foreach (var variable in namedVariables)
            foreach (var function in BuildNamedVariableFunctions(executionContext, scopedVariables, variable))
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
                (name) => GetVariable(scopedVariables, name)
            )
        );

        return ValueTask.CompletedTask;
    }

    private IEnumerable<IJavaScriptFunction> BuildNamedVariableFunctions(IJavaScriptExecutionContext context, IScopedVariableProvider? scopedVariables, IVariable variable)
    {
        var pascalName = variable.Name.Pascalize();

        yield return new JavaScriptFunction<object>(
            string.Format(WorkflowFunctionNames.SetNamedVariableFunctionFormat, pascalName),
            (value) => SetVariable(context, scopedVariables, variable.Name, value));

        yield return new JavaScriptFunction(
            string.Format(WorkflowFunctionNames.GetNamedVariableFunctionFormat, pascalName),
            () => GetVariable(scopedVariables, variable.Name));
    }

    private object? GetVariable(IScopedVariableProvider? scopedVariables, string name)
    {
        if (scopedVariables is not null && scopedVariables.TryGetVariableValueByName(name, out var value))
            return value;

        return workflowExecution.GetVariable(name);
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

        // Write back to the correct visible scope (nearest workflow/container scope declaring the
        // name) when a scope chain is available; otherwise to the workflow context.
        if (scopedVariables is not null && scopedVariables.TrySetVariableValueByName(name, value))
            return;

        workflowExecution.SetVariable(name, value);
    }
}