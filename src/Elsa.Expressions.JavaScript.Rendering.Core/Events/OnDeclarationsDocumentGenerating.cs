using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Rendering.Core.Events
{
    public sealed record OnDeclarationsDocumentGenerating(IJavaScriptRenderingContext Context) : IDomainEvent;
}
