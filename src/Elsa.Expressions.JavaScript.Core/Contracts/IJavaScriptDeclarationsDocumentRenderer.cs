
using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.Core.Contracts
{
    public interface IJavaScriptDeclarationsDocumentRenderer
    {
        string Render(JavaScriptDeclarationsDocument typeDocument);
    }
}
