using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Core.Services;

/// <summary>Resolves dotted metadata paths without evaluating an expression or reading runtime values.</summary>
public static class ExpressionToolingSymbolResolver
{
    public static IReadOnlyList<ExpressionToolingItem> Complete(
        ExpressionAuthoringContext context,
        string source,
        ExpressionToolingPosition position)
    {
        var steps = Steps(PathAt(source, position, includeRight: false));
        if (steps is null || steps[^1].IsIndex)
            return [];
        var roots = NormalizeRootSymbols(context.RootSymbols);
        if (steps.Count == 1)
            return roots
                .Where(symbol => symbol.Name.StartsWith(steps[0].Text, StringComparison.OrdinalIgnoreCase))
                .Select(ToItem)
                .ToArray();

        var shape = steps[0].IsIndex ? null : roots
            .FirstOrDefault(symbol => string.Equals(symbol.Name, steps[0].Text, StringComparison.Ordinal))
            ?.ValueShape;
        for (var index = 1; shape is not null && index < steps.Count - 1; index++)
            shape = Next(shape, steps[index], out _);
        var prefix = steps[^1].Text;
        return shape?.Members?
            .Where(member => member.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(member => new ExpressionToolingItem(
                member.Name,
                member.Shape.DisplayName,
                member.Documentation,
                member.Name,
                ExpressionSymbolKind.Member))
            .ToArray() ?? [];
    }

    public static ExpressionToolingItem? Resolve(
        ExpressionAuthoringContext context,
        string source,
        ExpressionToolingPosition position)
    {
        var steps = Steps(PathAt(source, position, includeRight: true))?
            .Where(step => step.IsIndex || step.Text.Length > 0)
            .ToArray();
        if (steps is null || steps.Length == 0 || steps[0].IsIndex)
            return null;
        var root = NormalizeRootSymbols(context.RootSymbols).FirstOrDefault(symbol =>
            string.Equals(symbol.Name, steps[0].Text, StringComparison.Ordinal));
        if (root is null)
            return null;
        if (steps.Length == 1)
            return ToItem(root);

        ExpressionValueMember? member = null;
        var shape = root.ValueShape;
        for (var index = 1; shape is not null && index < steps.Length; index++)
            shape = Next(shape, steps[index], out member);
        return member is null || shape is null
            ? null
            : new(member.Name, member.Shape.DisplayName, member.Documentation, member.Name, ExpressionSymbolKind.Member);
    }

    /// <summary>
    /// One step down a value shape. A name reads a member. An index reads an array item or a map value; on an
    /// object, a quoted key reads the member of that name.
    /// </summary>
    private static ExpressionValueShape? Next(ExpressionValueShape shape, PathStep step, out ExpressionValueMember? member)
    {
        member = null;
        if (step.IsIndex && (shape.Item ?? shape.Value) is { } item)
            return item;
        var name = step.IsIndex ? QuotedKey(step.Text) : step.Text;
        if (name is null)
            return null;
        member = shape.Members?.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        return member?.Shape;
    }

    private static string? QuotedKey(string text)
    {
        var key = text.Trim();
        return key.Length >= 2 && key[0] is '"' or '\'' && key[^1] == key[0] && key.IndexOf(key[0], 1) == key.Length - 1
            ? key[1..^1]
            : null;
    }

    /// <summary>Splits a path into names and bracketed indexes: <c>a.b[0].c</c> is a, b, [0], c. Null when malformed.</summary>
    private static List<PathStep>? Steps(string path)
    {
        var steps = new List<PathStep>();
        var start = 0;
        var expectName = true;
        for (var index = 0; index < path.Length; index++)
        {
            var character = path[index];
            if (character == '.')
            {
                if (expectName)
                    steps.Add(new(path[start..index], false));
                start = index + 1;
                expectName = true;
            }
            else if (character == '[')
            {
                if (expectName && (index > start || steps.Count == 0))
                    steps.Add(new(path[start..index], false));
                var close = path.IndexOf(']', index + 1);
                if (close < 0)
                    return null;
                steps.Add(new(path[(index + 1)..close], true));
                index = close;
                start = close + 1;
                expectName = false;
            }
            else if (!expectName)
                return null;
        }
        if (expectName)
            steps.Add(new(path[start..], false));
        return steps;
    }

    private readonly record struct PathStep(string Text, bool IsIndex);

    private static IReadOnlyList<ExpressionSymbol> NormalizeRootSymbols(
        IEnumerable<ExpressionSymbol> symbols) =>
        symbols
            .GroupBy(symbol => FirstSegment(symbol.Name), StringComparer.Ordinal)
            .Select(group =>
            {
                var direct = group.FirstOrDefault(symbol => !symbol.Name.Contains('.'));
                var nested = group
                    .Where(symbol => symbol.Name.Contains('.'))
                    .Select(symbol => new ExpressionValueMember(
                        symbol.Name[(symbol.Name.IndexOf('.') + 1)..],
                        symbol.ValueShape ?? new(),
                        symbol.Documentation))
                    .ToArray();
                if (nested.Length == 0 && direct is not null)
                    return direct with { ValueShape = NormalizeShape(direct.ValueShape) };

                var members = NormalizeMembers(nested);
                var shape = MergeMembers(direct?.ValueShape, group.Key, members);
                return direct is null
                    ? new($"namespace:{group.Key}", group.Key, ExpressionSymbolKind.Namespace, shape)
                    : direct with { ValueShape = shape };
            })
            .ToArray();

    private static IReadOnlyList<ExpressionValueMember> NormalizeMembers(
        IEnumerable<ExpressionValueMember> members) =>
        members
            .GroupBy(member => FirstSegment(member.Name), StringComparer.Ordinal)
            .Select(group =>
            {
                var direct = group.FirstOrDefault(member => !member.Name.Contains('.'));
                var nested = group
                    .Where(member => member.Name.Contains('.'))
                    .Select(member => member with
                    {
                        Name = member.Name[(member.Name.IndexOf('.') + 1)..]
                    })
                    .ToArray();
                if (nested.Length == 0)
                    return direct is null
                        ? null
                        : direct with { Shape = NormalizeShape(direct.Shape)! };

                return new ExpressionValueMember(
                    group.Key,
                    MergeMembers(direct?.Shape, group.Key, NormalizeMembers(nested)),
                    direct?.Documentation);
            })
            .Where(member => member is not null)
            .Select(member => member!)
            .ToArray();

    private static ExpressionValueShape MergeMembers(
        ExpressionValueShape? shape,
        string displayName,
        IReadOnlyList<ExpressionValueMember> nestedMembers)
    {
        var existing = NormalizeShape(shape)?.Members ?? [];
        var members = existing
            .Concat(nestedMembers)
            .GroupBy(member => member.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        return shape is null
            ? new(displayName, ExpressionValueKind.Object, false, Members: members)
            : shape with
            {
                Kind = ExpressionValueKind.Object,
                Members = members
            };
    }

    private static ExpressionValueShape? NormalizeShape(ExpressionValueShape? shape) =>
        shape?.Members is { Count: > 0 } members
            ? shape with { Members = NormalizeMembers(members) }
            : shape;

    private static string FirstSegment(string name)
    {
        var separator = name.IndexOf('.');
        return separator < 0 ? name : name[..separator];
    }

    private static ExpressionToolingItem ToItem(ExpressionSymbol symbol) =>
        new(
            symbol.Name,
            symbol.Signatures?.FirstOrDefault()?.Display ?? symbol.ValueShape?.DisplayName,
            symbol.Documentation,
            symbol.Name,
            symbol.Kind);

    private static string PathAt(string source, ExpressionToolingPosition position, bool includeRight)
    {
        var offset = ToOffset(source, position);
        var start = offset;
        var end = offset;
        while (start > 0)
        {
            if (IsPathCharacter(source[start - 1]))
                start--;
            else if (source[start - 1] == ']' && source.LastIndexOf('[', start - 1) is var open and >= 0 &&
                     source.AsSpan(open + 1, start - 1 - open - 1).IndexOfAny('\n', ']') < 0)
                start = open;
            else
                break;
        }
        // To the right, an index ends the path: it selects an item, not the name under the cursor.
        if (includeRight)
            while (end < source.Length && IsPathCharacter(source[end]))
                end++;
        return source[start..end];
    }

    private static int ToOffset(string source, ExpressionToolingPosition position)
    {
        if (!position.IsValid)
            return 0;
        var line = 0;
        var offset = 0;
        while (offset < source.Length && line < position.Line)
        {
            if (source[offset++] == '\n') line++;
        }
        return Math.Clamp(offset + position.Character, 0, source.Length);
    }

    private static bool IsPathCharacter(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or '$' or '.';
}
