using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Events;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Rendering.Services
{
    internal sealed class JavaScriptDeclarationsDocumentFactory(IDomainEventSender mediator)
        : IJavaScriptDeclarationsDocumentFactory
    {
        public async ValueTask<JavaScriptDeclarationsDocument> Create(CancellationToken cancellationToken = default)
        {
            var context = new JavaScriptDeclarationsContext();
            var domainEvent = new OnDeclarationsDocumentGenerating(context);
            await mediator.Send(domainEvent, cancellationToken);

            return new JavaScriptDeclarationsDocument
            {
                Functions = context.Functions,
                Variables = context.Variables,
                Types = context.Types
            };
        }
    }
}
