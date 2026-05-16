using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Workflows.Constants;
using System.Dynamic;
using System.Text.RegularExpressions;

namespace Elsa.Expressions.JavaScript.Workflows.Processors
{
    /// <summary>
    /// Copies variables from the workflow execution context into the JavaScript engine before evaluation, but only those variables that are actually referenced 
    /// in the expression. This optimizes the variable copying process by avoiding unnecessary copying of variables that are not used in the expression.
    /// </summary>
    internal sealed partial class CopyVariablesIntoEnginePreProcessor : IJavaScriptEvaluationPreProcessor
    {
        public ValueTask Process(IJavaScriptExecutionContext javascriptExecutionContext, IExpressionExecutionContext expressionExecutionContext, string expression)
        {
            CopyVariablesIntoEngine(javascriptExecutionContext, expressionExecutionContext, expression);
            return ValueTask.CompletedTask;
        }

        private static void CopyVariablesIntoEngine(IJavaScriptExecutionContext javascriptExecutionContext, IExpressionExecutionContext expressionExecutionContext, string expression)
        {
            var variableNames = GetUsedVariableNames(expressionExecutionContext, expression).ToList();
            var variablesContainer = (IDictionary<string, object?>)new ExpandoObject();

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
}
