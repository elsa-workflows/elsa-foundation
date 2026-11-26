using Elsa.Expressions.JavaScript.Rendering.Models;

namespace Elsa.Expressions.JavaScript.Rendering.Contracts;

internal interface IJavaScriptDeclarationsDocumentRenderer
{
    string Render(JavaScriptDeclarationsDocument typeDocument);
}