using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.Core
{
    public sealed class JavaScriptTypeDeclarationProvider(IEnumerable<JavaScriptTypeDeclaration> declarations) : IJavaScriptTypeDeclarationProvider
    {
        public ValueTask<IEnumerable<JavaScriptTypeDeclaration>> GetDeclarations(CancellationToken cancellationToken)
        {
            return new(declarations);
        }
    }
}
