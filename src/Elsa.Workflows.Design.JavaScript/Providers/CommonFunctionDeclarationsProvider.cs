using Elsa.Expressions.JavaScript.Core.Constants;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Workflows.Constants;

namespace Elsa.Workflows.Design.JavaScript.Providers
{
    internal sealed class CommonFunctionDeclarationsProvider : IJavaScriptFunctionDeclarationProvider
    {
        public ValueTask<IEnumerable<JavaScriptFunctionDeclaration>> GetDeclarations(CancellationToken cancellationToken = default)
        {
            var result = GetFunctionDeclarations();

            return new(result);
        }

        private static List<JavaScriptFunctionDeclaration> GetFunctionDeclarations()
        {
            var result = new List<JavaScriptFunctionDeclaration>
            {
                Build(WorkflowFunctionNames.GetWorkflowDefinitionId, WellKnownTypeNames.String),
                Build(WorkflowFunctionNames.GetWorkflowDefinitionVersionId, WellKnownTypeNames.String),

                Build(WorkflowFunctionNames.GetWorkflowDefinitionVersion, WellKnownTypeNames.Number),
                Build(WorkflowFunctionNames.GetWorkflowInstanceId, WellKnownTypeNames.String),
                Build(WorkflowFunctionNames.GetCorrelationId, WellKnownTypeNames.String),
                Build(WorkflowFunctionNames.SetCorrelationId, null, new JavaScriptParameterDeclaration("value", WellKnownTypeNames.String)),

                Build(WorkflowFunctionNames.GetWorkflowInstanceName, WellKnownTypeNames.String),
                Build(WorkflowFunctionNames.SetWorkflowInstanceName, null, new JavaScriptParameterDeclaration("value", WellKnownTypeNames.String)),


                Build(WorkflowFunctionNames.GetInput, WellKnownTypeNames.Any, new JavaScriptParameterDeclaration("name", WellKnownTypeNames.String)),
                Build(
                    WorkflowFunctionNames.GetOutputFrom,
                    returnType: WellKnownTypeNames.Any,
                    parameters: [
                        new JavaScriptParameterDeclaration("activityId", WellKnownTypeNames.String),
                        new JavaScriptParameterDeclaration("outputName", WellKnownTypeNames.Any, true)
                    ]),

                Build(WorkflowFunctionNames.GetLastResult, WellKnownTypeNames.Any),
            };

            return result;
        }

        static JavaScriptFunctionDeclaration Build(string name, string? returnType, IEnumerable<JavaScriptParameterDeclaration>? parameters = null)
        {
            return new JavaScriptFunctionDeclaration(name, returnType, parameters ?? []);
        }

        static JavaScriptFunctionDeclaration Build(string name, string? returnType, JavaScriptParameterDeclaration parameter)
            => Build(name, returnType, [parameter]);
    }
}
