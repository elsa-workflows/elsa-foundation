using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Workflows.Primitives.Constants;
using System.Text.RegularExpressions;

namespace Elsa.Workflows.Runtime.JavaScript.PreProcessors;

public sealed partial class WorkflowVariablesContextPreProcessor : IScriptPreProcessor
{
    public ValueTask PreProcess(string script, IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken)
    {
        CopyVariablesIntoEngine(executionContext, expressionContext, script);
        return ValueTask.CompletedTask;
    }

    private static void CopyVariablesIntoEngine(IJavaScriptExecutionContext javascriptExecutionContext, IExpressionExecutionContext expressionExecutionContext, string expression)
    {
        var variableNames = GetUsedVariableNames(expressionExecutionContext, expression).ToList();
        var variablesContainer = new Dictionary<string, object?>();

        foreach (var variableName in variableNames)
        {
            var variableValue = expressionExecutionContext.GetVariableInScope(variableName);
            var normalizedVariable = javascriptExecutionContext.NormalizeValue(variableValue);
            variablesContainer[variableName] = normalizedVariable;
        }

        var normalizedVariablesContainer = javascriptExecutionContext.NormalizeValue(variablesContainer);
        javascriptExecutionContext.SetValue(
            VariableNames.VariableContainer,
            normalizedVariablesContainer
        );
    }


    private static IEnumerable<string> GetUsedVariableNames(IExpressionExecutionContext context, string expression)
    {
        var variableNames = context
            .GetVariableNamesInScope()
            .FilterInvalidVariableNames();

        var variableNamesInScript = ExtractVariableNamesRegex().Matches(expression)
            .Select(m => m.Groups[1].Value)
            .ToList();

        return variableNames.Where(x => variableNamesInScript.Contains(x));
    }

    [GeneratedRegex(VariableNames.VariableContainer + @"\.(\w+)(?:\.\w+)*")]
    private static partial Regex ExtractVariableNamesRegex();
}