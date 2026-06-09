using Elsa.Expressions.JavaScript.Rendering.Core.Models;

namespace Elsa.Expressions.JavaScript.Rendering.Core.Contracts
{
    public interface IJavaScriptDeclarationsContributionContext
    {
        void AddVariable(JavaScriptVariableDeclaration declaration);

        void AddType(JavaScriptTypeDeclaration declaration);

        void AddFunction(JavaScriptFunctionDeclaration declaration);
    }
}
