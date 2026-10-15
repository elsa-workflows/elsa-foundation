using Elsa.Expressions.JavaScript.Rendering.Models;

namespace Elsa.Expressions.JavaScript.Rendering.Contracts;

internal interface IJavaScriptDeclarationsDocumentFactory
{
    ValueTask<JavaScriptDeclarationsDocument> Create(CancellationToken cancellationToken = default);
}