using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Liquid.Services;

internal enum LiquidCursorMode
{
    Text,
    Value,
    Filter,
    Tag,
    TagValue,
    String
}

internal readonly record struct LiquidCursorContext(
    LiquidCursorMode Mode,
    int TokenStart,
    int TokenEnd,
    int Offset)
{
    public string Prefix(string source) => source[TokenStart..Offset];

    public ExpressionToolingRange? TokenRange(string source) => TokenStart == TokenEnd
        ? null
        : new(
            LiquidCursorContextAnalyzer.ToPosition(source, TokenStart),
            LiquidCursorContextAnalyzer.ToPosition(source, TokenEnd));
}

/// <summary>Classifies the bounded lexical context around a cursor without evaluating Liquid.</summary>
internal static class LiquidCursorContextAnalyzer
{
    public static LiquidCursorContext Analyze(string source, ExpressionToolingPosition position)
    {
        var offset = ToOffset(source, position);
        var delimiterStart = -1;
        string? opaqueBlock = null;
        var inOutput = false;
        var inTag = false;
        var quote = '\0';
        var escaped = false;
        var lastPipe = -1;
        var lastColon = -1;

        for (var index = 0; index < offset; index++)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';

            if (!inOutput && !inTag)
            {
                if (opaqueBlock is not null)
                {
                    if (current == '{' && next == '%' && TryFindBlockEnd(source, index, opaqueBlock, out var blockEnd))
                    {
                        opaqueBlock = null;
                        index = blockEnd;
                    }

                    continue;
                }

                if (current != '{' || (next != '{' && next != '%'))
                    continue;

                delimiterStart = index;
                inOutput = next == '{';
                inTag = next == '%';
                lastPipe = -1;
                lastColon = -1;
                index++;
                continue;
            }

            if (quote != '\0')
            {
                if (escaped)
                    escaped = false;
                else if (current == '\\')
                    escaped = true;
                else if (current == quote)
                    quote = '\0';
                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
                continue;
            }

            if ((inOutput && current == '}' && next == '}') ||
                (inTag && current == '%' && next == '}'))
            {
                if (inTag)
                    opaqueBlock = ReadTagName(source, delimiterStart + 2, index) is { } tagName &&
                                  (tagName.Equals("raw", StringComparison.OrdinalIgnoreCase) ||
                                   tagName.Equals("comment", StringComparison.OrdinalIgnoreCase))
                        ? tagName
                        : null;

                inOutput = false;
                inTag = false;
                index++;
                continue;
            }

            if (inOutput)
            {
                if (current == '|') lastPipe = index;
                if (current == ':') lastColon = index;
            }
        }

        if (!inOutput && !inTag)
            return Empty(LiquidCursorMode.Text, offset);
        if (quote != '\0')
            return Empty(LiquidCursorMode.String, offset);

        var contentStart = delimiterStart + 2;
        if (inTag)
        {
            var first = SkipWhitespace(source, contentStart, offset);
            var firstEnd = IdentifierEnd(source, first, source.Length);
            var inTagName = offset <= firstEnd;
            var range = inTagName
                ? IdentifierRange(source, offset, first, Math.Max(firstEnd, offset))
                : IdentifierRange(source, offset, contentStart, offset);
            return new(inTagName ? LiquidCursorMode.Tag : LiquidCursorMode.TagValue, range.Start, range.End, offset);
        }

        var inFilterName = lastPipe >= 0 && lastColon < lastPipe;
        var valueRange = IdentifierRange(source, offset, contentStart, source.Length);
        return new(inFilterName ? LiquidCursorMode.Filter : LiquidCursorMode.Value, valueRange.Start, valueRange.End, offset);
    }

    public static ExpressionToolingPosition ToPosition(string source, int offset)
    {
        offset = Math.Clamp(offset, 0, source.Length);
        var line = 0;
        var lineStart = 0;
        for (var index = 0; index < offset; index++)
        {
            if (source[index] != '\n')
                continue;
            line++;
            lineStart = index + 1;
        }

        return new(line, offset - lineStart);
    }

    private static LiquidCursorContext Empty(LiquidCursorMode mode, int offset) =>
        new(mode, offset, offset, offset);

    private static int ToOffset(string source, ExpressionToolingPosition position)
    {
        if (!position.IsValid)
            return 0;

        var line = 0;
        var offset = 0;
        while (offset < source.Length && line < position.Line)
        {
            if (source[offset++] == '\n')
                line++;
        }

        return Math.Clamp(offset + position.Character, 0, source.Length);
    }

    private static int SkipWhitespace(string source, int offset, int limit)
    {
        while (offset < limit && char.IsWhiteSpace(source[offset]))
            offset++;
        return offset;
    }

    private static (int Start, int End) IdentifierRange(string source, int offset, int lowerBound, int upperBound)
    {
        lowerBound = Math.Clamp(lowerBound, 0, source.Length);
        upperBound = Math.Clamp(upperBound, lowerBound, source.Length);
        offset = Math.Clamp(offset, lowerBound, upperBound);
        var start = offset;
        var end = offset;
        while (start > lowerBound && IsIdentifierCharacter(source[start - 1])) start--;
        while (end < upperBound && IsIdentifierCharacter(source[end])) end++;
        return (start, end);
    }

    private static int IdentifierEnd(string source, int offset, int upperBound)
    {
        while (offset < upperBound && IsIdentifierCharacter(source[offset]))
            offset++;
        return offset;
    }

    private static string? ReadTagName(string source, int start, int end)
    {
        start = SkipWhitespace(source, start, end);
        if (start < end && source[start] == '-')
            start = SkipWhitespace(source, start + 1, end);
        var nameEnd = IdentifierEnd(source, start, end);
        return nameEnd == start ? null : source[start..nameEnd];
    }

    private static bool TryFindBlockEnd(string source, int start, string blockName, out int blockEnd)
    {
        blockEnd = -1;
        var closeStart = source.IndexOf("%}", start + 2, StringComparison.Ordinal);
        if (closeStart < 0)
            return false;

        var closeName = ReadTagName(source, start + 2, closeStart);
        if (!string.Equals(closeName, $"end{blockName}", StringComparison.OrdinalIgnoreCase))
            return false;

        blockEnd = closeStart + 1;
        return true;
    }

    private static bool IsIdentifierCharacter(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or '-' or '?';
}
