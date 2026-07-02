using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Workflows.Primitives.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.JavaScript.PreProcessors;

/// <summary>
/// Registers execution-time activity-output accessors (<c>getOutput(name)</c> and
/// <c>getOutputFrom(activity, name)</c>) from the live execution-time expression carrier
/// (<see cref="IExecutionExpressionState"/>). Re-pointed onto the carrier per ADR 0030: no DI-registered live
/// workflow execution context. The carrier's activity-output projection is keyed by output name (the runtime does
/// not durably carry the producing activity's runtime name), so the accessors are the generic name-based form —
/// the same surface the materialization-time processor provides, now available during activity execution
/// (ADR 0030 D3; the activity-name-qualified pascalized form is deferred, see spec 083 research R4). No-op for any
/// non-execution context, so it is safe to register globally.
/// </summary>
public sealed class ActivityOutputFunctionsPreProcessor : IScriptPreProcessor
{
    public ValueTask PreProcess(string script, IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken)
    {
        if (expressionContext is not IExecutionExpressionState state)
            return ValueTask.CompletedTask;

        executionContext.RegisterFunction(
            new JavaScriptFunction<string>("getOutput", name => state.ActivityOutputValues.GetValueOrDefault(name)));

        executionContext.RegisterFunction(
            new JavaScriptFunction<string, string>(WorkflowFunctionNames.GetOutputFrom, (_, name) => state.ActivityOutputValues.GetValueOrDefault(name)));

        return ValueTask.CompletedTask;
    }
}
