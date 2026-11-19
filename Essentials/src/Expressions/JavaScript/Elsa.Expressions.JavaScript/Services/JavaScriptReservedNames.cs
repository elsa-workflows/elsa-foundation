using Acornima.Ast;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Services;

/// <summary>
/// Finds declarations of <see cref="JavaScriptScriptBuiltIns.Complete"/>, a reserved built-in name in every JavaScript
/// surface (spec 1208 FR-007a, ADR 0045 §5): a variable, function, class, parameter or catch binding, through any
/// destructuring, default or rest pattern. Shared by the expression tooling and the script outcome analyzer.
/// </summary>
internal static class JavaScriptReservedNames
{
    public static IReadOnlyList<Identifier> CollectCompleteDeclarations(Node root)
    {
        var declarations = new List<Identifier>();
        Collect(root, declarations);
        return declarations;
    }

    /// <summary>The zero-based range of <paramref name="identifier"/>.</summary>
    public static ExpressionToolingRange RangeOf(Identifier identifier)
    {
        var start = identifier.Location.Start;
        var end = identifier.Location.End;
        return new(
            new(Math.Max(0, start.Line - 1), Math.Max(0, start.Column)),
            new(Math.Max(0, end.Line - 1), Math.Max(0, end.Column)));
    }

    private static void Collect(Node node, List<Identifier> declarations)
    {
        switch (node)
        {
            case VariableDeclarator declarator:
                CollectBindings(declarator.Id, declarations);
                break;
            case FunctionDeclaration { Id: { } functionId }:
                CollectBindings(functionId, declarations);
                break;
            case FunctionExpression { Id: { } functionExpressionId }:
                CollectBindings(functionExpressionId, declarations);
                break;
            case ClassDeclaration { Id: { } classId }:
                CollectBindings(classId, declarations);
                break;
            case ClassExpression { Id: { } classExpressionId }:
                CollectBindings(classExpressionId, declarations);
                break;
            case CatchClause { Param: { } parameter }:
                CollectBindings(parameter, declarations);
                break;
        }

        if (node is IFunction function)
        {
            foreach (var parameter in function.Params)
                CollectBindings(parameter, declarations);
        }

        foreach (var child in node.ChildNodes)
            Collect(child, declarations);
    }

    // The identifiers a binding pattern declares; a property key in a destructuring pattern declares nothing.
    private static void CollectBindings(Node? pattern, List<Identifier> declarations)
    {
        switch (pattern)
        {
            case Identifier identifier when StringComparer.Ordinal.Equals(identifier.Name, JavaScriptScriptBuiltIns.Complete):
                declarations.Add(identifier);
                break;
            case ObjectPattern objectPattern:
                foreach (var property in objectPattern.Properties)
                    CollectBindings(property is Property { Value: var value } ? value : property, declarations);
                break;
            case ArrayPattern arrayPattern:
                foreach (var element in arrayPattern.Elements)
                    CollectBindings(element, declarations);
                break;
            case AssignmentPattern assignment:
                CollectBindings(assignment.Left, declarations);
                break;
            case RestElement rest:
                CollectBindings(rest.Argument, declarations);
                break;
        }
    }
}
