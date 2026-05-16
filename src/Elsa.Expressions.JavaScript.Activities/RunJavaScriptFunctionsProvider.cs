using Elsa.Activities.Core.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Constants;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.Activities
{
    internal sealed class RunJavaScriptFunctionsProvider : IJavaScriptFunctionDeclarationProvider
    {
        private const string SetOutcomeFunctionName = "setOutcome";
        private const string SetOutcomesFunctionName = "setOutcomes";
        private const string NameParameterName = "name";

        public ValueTask<IEnumerable<JavaScriptFunctionDeclaration>> GetDeclarations(CancellationToken cancellationToken = default)
        {
            var results = new JavaScriptFunctionDeclaration[]
            {
                new (
                    SetOutcomeFunctionName, 
                    WellKnownTypeNames.Void, 
                    new JavaScriptParameterDeclaration(NameParameterName, WellKnownTypeNames.String)
                ),
                new (
                    SetOutcomesFunctionName,
                    WellKnownTypeNames.Void,
                    new JavaScriptParameterDeclaration(NameParameterName, WellKnownTypeNames.StringArray)
                )
            };

            return new(results);
        }

        internal static IEnumerable<IJavaScriptFunction> CreateSetOutcomeFunctions(IActivityExecutionContext context)
        {
            yield return new JavaScriptFunction<string>(SetOutcomeFunctionName, (value) => SetOutcome(context, value));
            yield return new JavaScriptFunction<string[]>(SetOutcomesFunctionName, (value) => SetOutcomes(context, value));
        }

        static object? SetOutcomes(IActivityExecutionContext ctx, string[] outcomes)
        {
            ctx.SetOutcomes(outcomes);
            return null;
        }

        static object? SetOutcome(IActivityExecutionContext ctx, string outcome)
        {
            ctx.SetOutcomes([outcome]);
            return null;
        }
    }
}
