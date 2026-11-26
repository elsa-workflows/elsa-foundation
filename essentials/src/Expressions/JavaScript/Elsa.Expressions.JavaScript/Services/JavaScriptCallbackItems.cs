using Acornima;
using Acornima.Ast;
using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.JavaScript.Services;

/// <summary>Projects item shapes into inline Array callbacks by reading syntax and authoring metadata.</summary>
internal static class JavaScriptCallbackItems
{
    public static ExpressionAuthoringContext Project(ExpressionAuthoringContext context, string source, ExpressionToolingPosition position)
    {
        if (!source.Contains("=>", StringComparison.Ordinal) && !source.Contains("function", StringComparison.Ordinal))
            return context;

        var offset = OffsetAt(source, position);
        if (offset == 0 || ParseAtCursor(source, offset) is not { } script)
            return context;

        var path = new List<Node>();
        FindContainingNodes(script, offset - 1, path);
        if (!path.Any(node => node is MemberExpression))
            return context;

        var locals = new Dictionary<string, ExpressionValueShape?>(StringComparer.Ordinal);
        var foundCallback = false;
        for (var index = 0; index < path.Count; index++)
        {
            var node = path[index];
            if (node is IFunction function)
            {
                var call = index > 0 ? path[index - 1] as CallExpression : null;
                var method = call is { Callee: MemberExpression member } && call.Arguments.Count > 0 &&
                             ReferenceEquals(call.Arguments[0], node) &&
                             ResolveShape(member.Object, context, locals) is { Kind: ExpressionValueKind.Array, Item: { } item }
                    ? (Name: MemberName(member), Item: item)
                    : default;
                if (function.Id is { } id)
                    locals[id.Name] = null;
                for (var parameterIndex = 0; parameterIndex < function.Params.Count; parameterIndex++)
                {
                    var parameter = function.Params[parameterIndex];
                    if (parameter is Identifier identifier && IsItemParameter(method.Name, parameterIndex))
                    {
                        locals[identifier.Name] = method.Item;
                        foundCallback = true;
                    }
                    else
                    {
                        MaskBindings(parameter, locals);
                    }
                }
                MaskHoistedVariables(function.Body, locals);
            }
            else if (node is Script)
            {
                MaskDeclarations(node, locals);
                MaskHoistedVariables(node, locals);
            }
            else if (node is BlockStatement or ForStatement or ForInStatement or ForOfStatement or SwitchCase)
            {
                MaskDeclarations(node, locals);
            }
            else if (node is CatchClause { Param: { } parameter })
            {
                MaskBindings(parameter, locals);
            }
        }

        if (!foundCallback)
            return context;

        var roots = context.RootSymbols.Where(symbol => !locals.ContainsKey(symbol.Name)).ToList();
        foreach (var (name, item) in locals)
            if (item is not null)
                roots.Add(new($"javascript:callback:{name}", name, ExpressionSymbolKind.Variable, item));
        return context with { RootSymbols = roots };
    }

    private static void MaskHoistedVariables(Node scope, Dictionary<string, ExpressionValueShape?> locals)
    {
        foreach (var child in scope.ChildNodes)
        {
            if (child is IFunction or ClassDeclaration or ClassExpression)
                continue;
            if (child is VariableDeclaration { Kind: VariableDeclarationKind.Var } declaration)
                foreach (var binding in declaration.Declarations)
                    MaskBindings(binding.Id, locals);
            else
                MaskHoistedVariables(child, locals);
        }
    }

    private static bool FindContainingNodes(Node node, int offset, List<Node> path)
    {
        if (offset < node.Start || offset >= node.End)
            return false;
        path.Add(node);
        foreach (var child in node.ChildNodes)
            if (FindContainingNodes(child, offset, path))
                break;
        return true;
    }

    private static void MaskDeclarations(Node scope, Dictionary<string, ExpressionValueShape?> locals)
    {
        foreach (var child in scope.ChildNodes)
        {
            switch (child)
            {
                case VariableDeclaration declaration:
                    foreach (var binding in declaration.Declarations)
                        MaskBindings(binding.Id, locals);
                    break;
                case FunctionDeclaration { Id: { } function }:
                    locals[function.Name] = null;
                    break;
                case ClassDeclaration { Id: { } type }:
                    locals[type.Name] = null;
                    break;
            }
        }
    }

    private static void MaskBindings(Node? pattern, Dictionary<string, ExpressionValueShape?> locals)
    {
        switch (pattern)
        {
            case Identifier identifier:
                locals[identifier.Name] = null;
                break;
            case AssignmentPattern assignment:
                MaskBindings(assignment.Left, locals);
                break;
            case RestElement rest:
                MaskBindings(rest.Argument, locals);
                break;
            case ObjectPattern or ArrayPattern:
                foreach (var element in pattern.ChildNodes)
                    MaskBindings(element is Property property ? property.Value : element, locals);
                break;
        }
    }

    private static bool IsItemParameter(string? method, int index) => method switch
    {
        "map" or "flatMap" or "filter" or "find" or "findLast" or "findIndex" or "findLastIndex" or "some" or "every" or "forEach" => index == 0,
        "reduce" or "reduceRight" => index == 1,
        "sort" or "toSorted" => index < 2,
        _ => false
    };

    private static ExpressionValueShape? ResolveShape(Expression expression, ExpressionAuthoringContext context, Dictionary<string, ExpressionValueShape?> locals)
    {
        switch (expression)
        {
            case Identifier identifier:
                return locals.TryGetValue(identifier.Name, out var local) ? local : FindRoot(context, identifier.Name)?.ValueShape;
            case CallExpression { Callee: Identifier function, Arguments.Count: 0 } when !locals.ContainsKey(function.Name):
                return FindRoot(context, function.Name)?.ValueShape;
            case MemberExpression member:
                var target = ResolveShape(member.Object, context, locals);
                if (member.Computed && (target?.Item ?? target?.Value) is { } indexed)
                    return indexed;
                var name = MemberName(member);
                return target?.Members?.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal))?.Shape;
            default:
                return null;
        }
    }

    private static ExpressionSymbol? FindRoot(ExpressionAuthoringContext context, string name) =>
        context.RootSymbols.FirstOrDefault(symbol => string.Equals(symbol.Name, name, StringComparison.Ordinal));

    private static string? MemberName(MemberExpression expression) => expression.Computed
        ? (expression.Property as StringLiteral)?.Value
        : (expression.Property as Identifier)?.Name;

    private static int OffsetAt(string source, ExpressionToolingPosition position)
    {
        var start = 0;
        for (var line = 0; line < position.Line; line++)
        {
            var newline = source.IndexOf('\n', start);
            if (newline < 0)
                return source.Length;
            start = newline + 1;
        }
        var end = source.IndexOf('\n', start);
        return start + Math.Clamp(position.Character, 0, (end < 0 ? source.Length : end) - start);
    }

    private static Script? ParseAtCursor(string source, int offset)
    {
        if (Parse(source) is { } complete)
            return complete;

        var prefix = source[..offset];
        if (prefix.EndsWith('.'))
        {
            prefix += "__elsaMember";
            if (Parse(prefix + source[offset..]) is { } withMember)
                return withMember;
        }

        // The parser's tokens keep delimiters in strings and regular expressions out of the balance.
        var tokens = new List<Token>();
        if (Parse(prefix, (in Token token) => tokens.Add(token)) is { } prefixScript)
            return prefixScript;

        var closers = new Stack<char>();
        foreach (var token in tokens)
        {
            if (token.Kind != TokenKind.Punctuator)
                continue;
            switch (token.StringValue)
            {
                case "(": closers.Push(')'); break;
                case "[": closers.Push(']'); break;
                case "{": closers.Push('}'); break;
                case ")" or "]" or "}":
                    if (!closers.TryPop(out var close) || close != token.StringValue[0])
                        return null;
                    break;
            }
        }
        return closers.Count == 0 ? null : Parse(prefix + "\n" + new string(closers.ToArray()));
    }

    private static Script? Parse(string source, OnTokenHandler? onToken = null)
    {
        try
        {
            return new Parser(new ParserOptions { AllowReturnOutsideFunction = true, OnToken = onToken }).ParseScript(source, strict: true);
        }
        catch (ParseErrorException)
        {
            return null;
        }
    }
}
