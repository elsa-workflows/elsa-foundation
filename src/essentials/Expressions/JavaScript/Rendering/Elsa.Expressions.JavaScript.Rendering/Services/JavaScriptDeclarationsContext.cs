using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;

namespace Elsa.Expressions.JavaScript.Rendering.Services;

internal sealed class JavaScriptDeclarationsContext : IJavaScriptDeclarationsContributionContext
{
    private readonly List<JavaScriptFunctionDeclaration> functions = [];
    private readonly List<JavaScriptTypeDeclaration> types = [];
    private readonly List<JavaScriptVariableDeclaration> variables = [];

    internal IEnumerable<JavaScriptFunctionDeclaration> Functions => functions.AsEnumerable();
    internal IEnumerable<JavaScriptTypeDeclaration> Types => types.AsEnumerable();
    internal IEnumerable<JavaScriptVariableDeclaration> Variables => variables.AsEnumerable();

    public void AddFunction(JavaScriptFunctionDeclaration declaration)
    {
        functions.Add(declaration);
    }

    public void AddType(JavaScriptTypeDeclaration declaration)
    {
        types.Add(declaration);
    }

    public void AddVariable(JavaScriptVariableDeclaration declaration)
    {
        variables.Add(declaration);
    }
}