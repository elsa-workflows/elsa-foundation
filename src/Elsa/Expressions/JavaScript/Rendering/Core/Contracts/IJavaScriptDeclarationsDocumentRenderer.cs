using Elsa.Expressions.JavaScript.Rendering.Core.Models;

namespace Elsa.Expressions.JavaScript.Rendering.Core.Contracts;

public interface IJavaScriptDeclarationsDocumentRenderer
{
    string Render(JavaScriptDeclarationsDocument typeDocument);
}