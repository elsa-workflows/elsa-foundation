using Elsa.Expressions.JavaScript.Primitives.Constants;
using Elsa.Expressions.JavaScript.Rendering.Core.Events;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Primitives.Constants;

namespace Elsa.Workflows.Design.JavaScript.EventHandlers
{
    public sealed class AddFunctionDeclarations : IDomainEventHandler<OnDeclarationsDocumentGenerating>
    {
        private const string NameParameterName = "name";

        public ValueTask Handle(OnDeclarationsDocumentGenerating domainEvent, CancellationToken cancellationToken)
        {
            domainEvent.Context.AddFunction(
                new(
                    ActivityFunctionNames.SetOutcome,
                    WellKnownTypeNames.Void,
                    new JavaScriptParameterDeclaration(NameParameterName, WellKnownTypeNames.String)
                )
            );
            domainEvent.Context.AddFunction(
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
