using Elsa.Expressions.JavaScript.Rendering.Core.Models;

namespace Elsa.Expressions.JavaScript.Rendering.Core.Options;

public sealed class JavaScriptDeclarationOptions
{
    public bool DisableWrappers { get; set; }

    public ICollection<JavaScriptFunctionDeclaration> Functions { get; } = [];

    public ICollection<JavaScriptVariableDeclaration> VariableDeclarations { get; } = [];

    public ICollection<JavaScriptTypeDeclaration> TypeDeclarations { get; } = [];

    public string GetTypeAliasOrDefault(string typeName)
    {
        var declaration = TypeDeclarations.FirstOrDefault(x => x.Name == typeName);
        return declaration?.Alias ?? typeName;
    }
}