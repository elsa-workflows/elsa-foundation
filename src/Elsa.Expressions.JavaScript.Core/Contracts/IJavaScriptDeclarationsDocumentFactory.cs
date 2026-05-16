using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.Core.Contracts
{
    public interface IJavaScriptDeclarationsDocumentFactory
    {
        ValueTask<JavaScriptDeclarationsDocument> Create(CancellationToken cancellationToken = default);
    }
}
