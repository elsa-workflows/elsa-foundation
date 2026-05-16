using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.Core
{
    public sealed class JavaScriptVariableDeclarationProvider(IEnumerable<JavaScriptVariableDeclaration> declarations) : IJavaScriptVariableDeclarationProvider
    {
        public ValueTask<IEnumerable<JavaScriptVariableDeclaration>> GetDeclarations(CancellationToken cancellationToken)
        {
            return new(declarations);
        }
    }
}
