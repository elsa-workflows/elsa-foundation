using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.Rendering.Core.Contracts
{
    public interface IJavaScriptDeclarationsDocumentRenderer
    {
        string Render(JavaScriptDeclarationsDocument typeDocument);
    }
}
