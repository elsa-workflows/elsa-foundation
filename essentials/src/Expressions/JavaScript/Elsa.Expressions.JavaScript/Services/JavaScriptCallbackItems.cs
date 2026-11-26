using Acornima;
using Acornima.Ast;
using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.JavaScript.Services;

/// <summary>Projects item shapes into inline Array callbacks by reading syntax and authoring metadata.</summary>
internal static class JavaScriptCallbackItems
{
    public static ExpressionAuthoringContext Project(ExpressionAuthoringContext context, string source, ExpressionToolingPosition position, CancellationToken cancellationToken)
    {
        if (!source.Contains("=>", StringComparison.Ordinal) && !source.Contains("function", StringComparison.Ordinal))
            return context;

        var offset = OffsetAt(source, position);
        if (offset == 0 || ParseAtCursor(source, offset, cancellationToken) is not { } script)
            return context;

        var path = FindContainingNodes(script, offset - 1, cancellationToken);
        if (!path.OfType<MemberExpression>().Any(member => IsAtMember(source, offset, member)))
            return context;

        var locals = new Dictionary<string, ExpressionValueShape?>(StringComparer.Ordinal);
        var foundCallback = false;
        for (var index = 0; index < path.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = path[index];
            if (node is IFunction function)
            {
                var call = index > 0 ? path[index - 1] as CallExpression : null;
                var method = call is { Callee: MemberExpression member } && call.Arguments.Count > 0 &&
                             ReferenceEquals(call.Arguments[0], node) &&
                             ResolveShape(member.Object, context, locals) is { Kind: ExpressionValueKind.Array, Item: { } item }
                    ? (Name: MemberName(member), Item: item)
                    : default;
                HashSet<string>? itemParameters = null;
                if (function.Id is { } id)
                    locals[id.Name] = null;
                for (var parameterIndex = 0; parameterIndex < function.Params.Count; parameterIndex++)
                {
                    var parameter = function.Params[parameterIndex];
                    if (parameter is Identifier identifier && IsItemParameter(method.Name, parameterIndex))
                    {
                        locals[identifier.Name] = method.Item;
                        (itemParameters ??= new(StringComparer.Ordinal)).Add(identifier.Name);
                        foundCallback = true;
                    }
                    else
                    {
                        MaskBindings(parameter, locals);
                    }
                }
                MaskHoistedVariables(function.Body, locals, itemParameters, cancellationToken);
            }
            else if (node is Script)
            {
                MaskDeclarations(node, locals);
                MaskHoistedVariables(node, locals, null, cancellationToken);
            }
            else if (node is BlockStatement or ForStatement or ForInStatement or ForOfStatement)
            {
                MaskDeclarations(node, locals);
            }
            else if (node is SwitchStatement block && index + 1 < path.Count && path[index + 1] is SwitchCase)
            {
                foreach (var clause in block.Cases)
                    MaskDeclarations(clause, locals);
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

    private static bool IsAtMember(string source, int offset, MemberExpression member) =>
        member is { Computed: false, Property: Identifier } &&
        (member.Property.Start < offset && offset <= member.Property.End ||
         member.Property.Start == offset && source[offset - 1] == '.');

    private static void MaskHoistedVariables(Node scope, Dictionary<string, ExpressionValueShape?> locals,
        HashSet<string>? itemParameters, CancellationToken cancellationToken)
    {
        if (scope is IFunction or ClassDeclaration or ClassExpression)
            return;
        var pending = new Stack<Node>();
        pending.Push(scope);
        while (pending.TryPop(out var node))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is VariableDeclaration { Kind: VariableDeclarationKind.Var } declaration)
            {
                foreach (var binding in declaration.Declarations)
                {
                    // An uninitialized var with the same name reuses this function's parameter.
                    if (binding.Init is null && binding.Id is Identifier id && itemParameters?.Contains(id.Name) == true)
                        continue;
                    MaskBindings(binding.Id, locals);
                }
            }
            else
            {
                foreach (var child in node.ChildNodes)
                    if (child is not (IFunction or ClassDeclaration or ClassExpression))
                        pending.Push(child);
            }
        }
    }

    private static List<Node> FindContainingNodes(Node root, int offset, CancellationToken cancellationToken)
    {
        var path = new List<Node>();
        Node? current = root;
        while (current is not null && offset >= current.Start && offset < current.End)
        {
            cancellationToken.ThrowIfCancellationRequested();
            path.Add(current);
            var parent = current;
            current = null;
            foreach (var child in parent.ChildNodes)
            {
                if (offset < child.Start || offset >= child.End)
                    continue;
                current = child;
                break;
            }
        }
        return path;
    }

    private static void MaskDeclarations(Node scope, Dictionary<string, ExpressionValueShape?> locals)
    {
        foreach (var child in scope.ChildNodes)
        {
            switch (child)
            {
                case VariableDeclaration { Kind: not VariableDeclarationKind.Var } declaration:
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
        if (pattern is null)
            return;
        var pending = new Stack<Node>();
        pending.Push(pattern);
        while (pending.TryPop(out var node))
        {
            switch (node)
            {
                case Identifier identifier:
                    locals[identifier.Name] = null;
                    break;
                case AssignmentPattern assignment:
                    pending.Push(assignment.Left);
                    break;
                case RestElement rest:
                    pending.Push(rest.Argument);
                    break;
                case ObjectPattern or ArrayPattern:
                    foreach (var element in node.ChildNodes)
                        pending.Push(element is Property property ? property.Value : element);
                    break;
            }
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
        var path = new Stack<MemberExpression>();
        while (expression is MemberExpression member)
        {
            path.Push(member);
            expression = member.Object;
        }
        var shape = expression switch
        {
            Identifier id => locals.TryGetValue(id.Name, out var local) ? local : FindRoot(context, id.Name)?.ValueShape,
            CallExpression { Callee: Identifier function, Arguments.Count: 0 } when !locals.ContainsKey(function.Name) => FindRoot(context, function.Name)?.ValueShape,
            _ => null
        };
        while (shape is not null && path.TryPop(out var step))
        {
            var name = MemberName(step);
            shape = step.Computed && (shape.Item ?? shape.Value) is { } indexed
                ? indexed
                : shape.Members?.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal))?.Shape;
        }
        return shape;
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

    private static Script? ParseAtCursor(string source, int offset, CancellationToken cancellationToken)
    {
        if (Parse(source, cancellationToken) is { } complete)
            return complete;

        var prefix = source[..offset];
        if (prefix.EndsWith('.'))
        {
            prefix += "__elsaMember";
            if (Parse(prefix + source[offset..], cancellationToken) is { } withMember)
                return withMember;
        }

        // The parser's tokens keep delimiters in strings and regular expressions out of the balance.
        var tokens = new List<Token>();
        if (Parse(prefix, cancellationToken, tokens) is { } prefixScript)
            return prefixScript;

        var closers = new Stack<char>();
        foreach (var token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token.Kind != TokenKind.Punctuator)
                continue;
            switch (token.StringValue)
            {
                case "(": closers.Push(')'); break;
                case "[": closers.Push(']'); break;
                case "{" or "${": closers.Push('}'); break;
                case "`":
                    if (closers.TryPeek(out var top) && top == '`') closers.Pop();
                    else closers.Push('`');
                    break;
                case ")" or "]" or "}":
                    if (!closers.TryPop(out var close) || close != token.StringValue[0])
                        return null;
                    break;
            }
        }
        return closers.Count == 0 ? null : Parse(prefix + "\n" + new string(closers.ToArray()), cancellationToken);
    }

    private static Script? Parse(string source, CancellationToken cancellationToken, List<Token>? tokens = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var options = new ParserOptions
            {
                AllowReturnOutsideFunction = true,
                OnToken = (in Token token) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    tokens?.Add(token);
                }
            };
            return new Parser(options).ParseScript(source, strict: true);
        }
        catch (ParseErrorException)
        {
            return null;
        }
    }
}
