namespace Elsa.Expressions.JavaScript.Rendering.Core.Models;

/// <summary>
/// A method definition.
/// </summary>
public sealed class JavaScriptFunctionDeclaration
{
    /// <summary>
    /// Constructor.
    /// </summary>
    public JavaScriptFunctionDeclaration()
    {
    }

    /// <summary>
    /// Constructor.
    /// </summary>
    public JavaScriptFunctionDeclaration(string name, IEnumerable<JavaScriptParameterDeclaration> parameters)
    {
        Name = name;
        Parameters = parameters.ToList();
    }

    /// <summary>
    /// Constructor.
    /// </summary>
    public JavaScriptFunctionDeclaration(string name, string? returnType, IEnumerable<JavaScriptParameterDeclaration> parameters)
    {
        Name = name;
        ReturnType = returnType;
        Parameters = [.. parameters];
    }

    public JavaScriptFunctionDeclaration(string name, string? returnType, JavaScriptParameterDeclaration parameter)
    {
        Name = name;
        ReturnType = returnType;
        Parameters = [parameter];
    }

    /// <summary>
    /// Constructor.
    /// </summary>
    public JavaScriptFunctionDeclaration(string name, string returnType)
    {
        Name = name;
        ReturnType = returnType;
        Parameters = [];
    }

    /// <summary>
    /// The name of the method.
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// The return type name of the method.
    /// </summary>
    public string? ReturnType { get; set; }

    /// <summary>
    /// The parameters of the method.
    /// </summary>
    public ICollection<JavaScriptParameterDeclaration> Parameters { get; set; } = [];
}