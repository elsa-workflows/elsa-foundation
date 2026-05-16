using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.Core.Contracts
{
    /// <summary>
    /// Provides <see cref="VariableDefinition"/>s to the type definition document being constructed.
    /// </summary>
    public interface IJavaScriptVariableDeclarationProvider
    {
        ValueTask<IEnumerable<JavaScriptVariableDeclaration>> GetDeclarations(CancellationToken cancellationToken);
    }    
}
