namespace Elsa.Expressions.JavaScript.Rendering.Core.Models;

/// <summary>
/// Represents a variable definition.
/// </summary>
public sealed class JavaScriptVariableDeclaration
{
    public JavaScriptVariableDeclaration()
    {
    }

    public JavaScriptVariableDeclaration(string name, string type, string declarationKeyword = "var")
    {
        Name = name;
        Type = type;
        DeclarationKeyword = declarationKeyword;
    }

    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string DeclarationKeyword { get; set; } = "var";
}
