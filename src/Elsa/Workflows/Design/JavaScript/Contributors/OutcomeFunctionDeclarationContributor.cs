using Elsa.Expressions.JavaScript.Primitives.Constants;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;
using Elsa.Workflows.Primitives.Constants;

namespace Elsa.Workflows.Design.JavaScript.Contributors
{
    public sealed class OutcomeFunctionDeclarationContributor : IJavaScriptDeclarationContributor
    {
        private const string NameParameterName = "name";

        public ValueTask Contribute(IJavaScriptDeclarationsContributionContext context, CancellationToken cancellationToken)
        {
            context.AddFunction(
                new(
                    ActivityFunctionNames.SetOutcome,
                    WellKnownTypeNames.Void,
                    new JavaScriptParameterDeclaration(NameParameterName, WellKnownTypeNames.String)
                )
            );
            context.AddFunction(
                 new(
                    ActivityFunctionNames.SetOutcomes,
                    WellKnownTypeNames.Void,
                    new JavaScriptParameterDeclaration(NameParameterName, WellKnownTypeNames.StringArray)
                )
            );

            return ValueTask.CompletedTask;
        }
    }
}
