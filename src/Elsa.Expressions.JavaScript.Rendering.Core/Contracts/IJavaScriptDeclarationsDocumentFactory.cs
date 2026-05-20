using Elsa.Expressions.JavaScript.Rendering.Core.Models;

namespace Elsa.Expressions.JavaScript.Rendering.Core.Contracts
{
    public interface IJavaScriptDeclarationsDocumentFactory
    {
        ValueTask<JavaScriptDeclarationsDocument> Create(CancellationToken cancellationToken = default);
    }
}
