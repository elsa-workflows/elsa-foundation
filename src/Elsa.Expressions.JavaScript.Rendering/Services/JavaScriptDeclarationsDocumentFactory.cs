using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Events;
using Elsa.Mediator.Core;

namespace Elsa.Expressions.JavaScript.Rendering.Services
{
    internal sealed class JavaScriptDeclarationsDocumentFactory(IMediator mediator)
        : IJavaScriptDeclarationsDocumentFactory
    {
        public async ValueTask<JavaScriptDeclarationsDocument> Create(CancellationToken cancellationToken = default)
        {
            var context = new JavaScriptDeclarationsContext();
            var domainEvent = new OnDeclarationsDocumentGenerating(context);
            await mediator.Publish(domainEvent, cancellationToken);

            return new JavaScriptDeclarationsDocument
            {
                Functions = context.Functions,
                Variables = context.Variables,
                Types = context.Types
            };
        }
    }
}
