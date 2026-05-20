using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Primitives.Constants;
using Elsa.Expressions.JavaScript.Rendering.Core.Events;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Design.Core;

namespace Elsa.Workflows.Design.JavaScript.EventHandlers
{
    internal sealed class AddWorkflowInputFunctionDeclarations(IWorkflowDesignContext designContext) : IDomainEventHandler<OnDeclarationsDocumentGenerating>
    {
        public ValueTask Handle(OnDeclarationsDocumentGenerating domainEvent, CancellationToken cancellationToken)
        {
            var inputs = designContext.Inputs.Where(x => VariableNameValidator.IsValidVariableName(x.Name));
            foreach (var input in inputs)
            {
                var pascalizedInputName = input.Name.Pascalize();
                var getInputValue = new JavaScriptFunctionDeclaration(
                    $"get{pascalizedInputName}",
                    WellKnownTypeNames.Any
                );

                domainEvent.Context.AddFunction(getInputValue);
            }
            return ValueTask.CompletedTask;
        }
    }
}
