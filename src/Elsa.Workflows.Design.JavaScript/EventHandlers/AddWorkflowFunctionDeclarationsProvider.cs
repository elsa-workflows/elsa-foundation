using Elsa.Expressions.JavaScript.Primitives.Constants;
using Elsa.Expressions.JavaScript.Rendering.Core.Events;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Primitives.Constants;

namespace Elsa.Workflows.Design.JavaScript.EventHandlers
{
    internal sealed class AddWorkflowFunctionDeclarationsProvider : IDomainEventHandler<OnDeclarationsDocumentGenerating>
    {
        public ValueTask Handle(OnDeclarationsDocumentGenerating domainEvent, CancellationToken cancellationToken)
        {
            GetFunctionDeclarations().ForEach(domainEvent.Context.AddFunction);
            return ValueTask.CompletedTask;
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

        private static JavaScriptFunctionDeclaration Build(string name, string? returnType, IEnumerable<JavaScriptParameterDeclaration>? parameters = null)
        {
            return new JavaScriptFunctionDeclaration(name, returnType, parameters ?? []);
        }

        private static JavaScriptFunctionDeclaration Build(string name, string? returnType, JavaScriptParameterDeclaration parameter)
            => Build(name, returnType, [parameter]);
    }
}
