using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Primitives.Constants;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;
using Elsa.Expressions.JavaScript.Rendering.Core.Options;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.JavaScript.Contributors
{
    public sealed class ActivityOutputFunctionDeclarationContributor(IOptions<JavaScriptDeclarationOptions> options, IWorkflowDesignContext designContext)
        : IJavaScriptDeclarationContributor
    {
        public ValueTask Contribute(IJavaScriptDeclarationsContributionContext context, CancellationToken cancellationToken)
        {
            if (options.Value.DisableWrappers)
                return ValueTask.CompletedTask;

            var activitiesWithOutput = designContext.GetActivitiesWithOutput();

            foreach (var activity in activitiesWithOutput)
            {
                var activityName = $"{activity.Definition.ActivityTypeKey?.Pascalize()}";
                var outputs = activity.Outputs
                    .Where(x => VariableNameValidator.IsValidVariableName(x.Name))
                    .Select(x => x.Name.Pascalize());

                outputs
                    .Select(output => new JavaScriptFunctionDeclaration($"get{output}From{activityName}", WellKnownTypeNames.Any))
                    .ToList()
                    .ForEach(context.AddFunction);
            }

            return ValueTask.CompletedTask;
        }
    }
}
