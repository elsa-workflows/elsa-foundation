using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Events;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Rendering.Services;

internal sealed class JavaScriptDeclarationsDocumentFactory(IInlineEventPublisher eventPublisher)
    : IJavaScriptDeclarationsDocumentFactory
{
    public async ValueTask<JavaScriptDeclarationsDocument> Create(CancellationToken cancellationToken = default)
    {
        var context = new JavaScriptDeclarationsContext();
        var domainEvent = new DeclarationsDocumentGenerating(context);
        await eventPublisher.Publish(domainEvent, cancellationToken);

        return new JavaScriptDeclarationsDocument
        {
            Functions = context.Functions,
            Variables = context.Variables,
            Types = context.Types
        };
    }
}