using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.Core.Contracts
{
    public interface IJavaScriptFunctionDeclarationProvider
    {
        ValueTask<IEnumerable<JavaScriptFunctionDeclaration>> GetDeclarations(CancellationToken cancellationToken = default);
    }
}
