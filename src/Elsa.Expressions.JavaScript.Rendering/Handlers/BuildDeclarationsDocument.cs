using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Events;
using Elsa.Events.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Rendering.Handlers
{
    /// <summary>
    /// Single handler for <see cref="OnDeclarationsDocumentGenerating"/>. Iterates every registered
    /// <see cref="IJavaScriptDeclarationContributor"/> and lets each contribute to the rendering context.
    /// </summary>
    public sealed class BuildDeclarationsDocument(IEnumerable<IJavaScriptDeclarationContributor> contributors)
        : IEventHandler<OnDeclarationsDocumentGenerating>
    {
        public async Task Handle(OnDeclarationsDocumentGenerating domainEvent, CancellationToken cancellationToken)
        {
            foreach (var contributor in contributors)
                await contributor.Contribute(domainEvent.Context, cancellationToken);
        }
    }
}
