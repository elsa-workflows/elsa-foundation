using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Primitives.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.JavaScript.PreProcessors;

/// <summary>
/// Registers named workflow-input accessors (e.g. <c>getName()</c> for an input named <c>name</c>) from the
/// live execution-time expression carrier (<see cref="IExecutionExpressionState"/>). Re-pointed onto the carrier
/// per ADR 0030: it no longer depends on a DI-registered live workflow execution context. It is a no-op for any
/// evaluation whose context is not the execution-time carrier (e.g. input materialization), so it is safe to
/// register globally.
/// </summary>
public sealed class WorkflowInputFunctionsPreProcessor : IScriptPreProcessor
{
    public ValueTask PreProcess(string script, IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken)
    {
        if (expressionContext is not IExecutionExpressionState state)
            return ValueTask.CompletedTask;

        // Create workflow input accessors - only if the current activity is not part of a composite activity definition.
        // Otherwise, the workflow input accessors will hide the composite activity input accessors which rely on variable accessors.
        if (expressionContext.IsContainedWithinCompositeActivity())
            return ValueTask.CompletedTask;

        foreach (var (inputName, value) in state.WorkflowInputs)
        {
            var name = string.Format(
                WorkflowFunctionNames.GetNamedInputFunctionFormat,
                inputName.Pascalize()
            );

            executionContext.RegisterFunction(new JavaScriptFunction(name, () => value));
        }

        return ValueTask.CompletedTask;
    }
}
