using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Core.Constants;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Core.Options;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Design.Core;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.JavaScript.Providers
{
    internal sealed class ActivityOutputFunctionDeclarationsProvider(IOptions<JavaScriptProviderOptions> options, IWorkflowDesignContext designContext) 
        : IJavaScriptFunctionDeclarationProvider
    {
        public async ValueTask<IEnumerable<JavaScriptFunctionDeclaration>> GetDeclarations(CancellationToken cancellationToken = default)
        {
            if (options.Value.DisableWrappers)
                return [];

            var activitiesWithOutput = designContext.Graph.ActivityNodes
                .Select(x => x.Definition)
                .Where(x => x.Name.IsValidVariableName());

            var result = new List<JavaScriptFunctionDeclaration>();

            foreach (var activity in activitiesWithOutput)
            {
                var activityName = $"{activity.Name?.Pascalize()}";
                var outputs = activity.Outputs
                    .Where(x => VariableNameValidator.IsValidVariableName(x.Name))
                    .Select(x => x.Name.Pascalize());

                var declarations = outputs
                    .Select(output => new JavaScriptFunctionDeclaration($"get{output}From{activityName}", WellKnownTypeNames.Any));

                result.AddRange(declarations);
            }

            return result;
        }
    }
}
