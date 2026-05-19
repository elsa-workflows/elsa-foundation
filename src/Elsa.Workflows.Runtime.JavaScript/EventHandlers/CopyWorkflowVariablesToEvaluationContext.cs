using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Mediator.Core;
using Elsa.Workflows.Constants;
using System.Dynamic;
using System.Text.RegularExpressions;

namespace Elsa.Workflows.Runtime.JavaScript.EventHandlers
{
    public sealed partial class CopyWorkflowVariablesToEvaluationContext : IDomainEventHandler<OnEvaluatingScript>
    {
        public ValueTask Handle(OnEvaluatingScript domainEvent, CancellationToken cancellationToken)
        {
            var variableNames = GetUsedVariableNames(domainEvent).ToList();
            var variablesContainer = (IDictionary<string, object?>)new ExpandoObject();

            foreach (var variableName in variableNames)
            {
                var variableValue = domainEvent.ExpressionContext.GetVariableInScope(variableName);
                variablesContainer[variableName] = variableValue;
            }

            domainEvent.EvaluationContext.SetValue(
                VariableNames.VariableContainer,
                variablesContainer
            );

            return ValueTask.CompletedTask;
        }


        private static IEnumerable<string> GetUsedVariableNames(OnEvaluatingScript domainEvent)
        {
            var variableNames = domainEvent.ExpressionContext
                .GetVariableNamesInScope()
                .FilterInvalidVariableNames();

            var variableNamesInScript = ExtractVariableNamesRegex().Matches(domainEvent.Script)
                .Select(m => m.Groups[1].Value)
                .ToList();

            return variableNames.Where(x => variableNamesInScript.Contains(x));
        }

        [GeneratedRegex(VariableNames.VariableContainer + @"\.(\w+)(?:\.\w+)*")]
        private static partial Regex ExtractVariableNamesRegex();
    }
}
