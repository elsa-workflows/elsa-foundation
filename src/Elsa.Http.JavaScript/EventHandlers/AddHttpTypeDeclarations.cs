using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Events;
using Elsa.Http.JavaScript.Constants;
using Elsa.Mediator.Core;

namespace Elsa.Http.JavaScript.EventHandlers
{
    public sealed class AddHttpTypeDeclarations(IJavaScriptTypeDeclarationFactory typeDeclarationFactory)
        : IDomainEventHandler<OnDeclarationsDocumentGenerating>
    {
        public ValueTask Handle(OnDeclarationsDocumentGenerating domainEvent, CancellationToken cancellationToken)
        {
            HttpTypeDescriptors
                .GetDescriptors()
                .Select(d => typeDeclarationFactory.Create(d.GetDescriptorType()))                             
                .ToList()
                .ForEach(domainEvent.Context.AddType);

            HttpTypeDescriptors
                .GetAliases()
                .Select(x =>
                {
                    var result = typeDeclarationFactory.Create(x.type);
                    result.Alias = x.alias;
                    return result;
                })
                .ToList()
                .ForEach(domainEvent.Context.AddType);

            return ValueTask.CompletedTask;
        }
    }
}
