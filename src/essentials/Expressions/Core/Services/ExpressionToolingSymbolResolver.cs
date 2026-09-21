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
        var path = PathAt(source, position, includeRight: false);
        var segments = path.Split('.', StringSplitOptions.None);
        var roots = NormalizeRootSymbols(context.RootSymbols);
        if (segments.Length == 1)
            return roots
                .Where(symbol => symbol.Name.StartsWith(segments[0], StringComparison.OrdinalIgnoreCase))
                .Select(ToItem)
                .ToArray();

        var shape = roots
            .FirstOrDefault(symbol => string.Equals(symbol.Name, segments[0], StringComparison.Ordinal))
            ?.ValueShape;
        for (var index = 1; shape is not null && index < segments.Length - 1; index++)
            shape = shape.Members?
                .FirstOrDefault(member => string.Equals(member.Name, segments[index], StringComparison.Ordinal))
                ?.Shape;
        var prefix = segments[^1];
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
        var segments = PathAt(source, position, includeRight: true)
            .Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return null;
        var root = NormalizeRootSymbols(context.RootSymbols).FirstOrDefault(symbol =>
            string.Equals(symbol.Name, segments[0], StringComparison.Ordinal));
        if (root is null)
            return null;
        if (segments.Length == 1)
            return ToItem(root);

        ExpressionValueMember? member = null;
        var shape = root.ValueShape;
        for (var index = 1; shape is not null && index < segments.Length; index++)
        {
            member = shape.Members?.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, segments[index], StringComparison.Ordinal));
            shape = member?.Shape;
        }
        return member is null
            ? null
            : new(member.Name, member.Shape.DisplayName, member.Documentation, member.Name, ExpressionSymbolKind.Member);
    }

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
        while (start > 0 && IsPathCharacter(source[start - 1])) start--;
        if (includeRight)
            while (end < source.Length && IsPathCharacter(source[end])) end++;
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
