using Elsa.Expressions.JavaScript.Activities.Constants;
using Elsa.Expressions.JavaScript.Core.Constants;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Rendering.Core.Events;
using Elsa.Mediator.Core;

namespace Elsa.Expressions.JavaScript.Activities.EventHandlers
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
