namespace Elsa.Expressions.Core.Extensions;

public static class VariableNameValidator
{
    public static bool IsValidVariableName(this string? name)
    {
        // Names are emitted as JavaScript/TypeScript identifiers, so they must start with an
        // identifier-start character and continue with identifier-continue characters (#408).
        return !string.IsNullOrEmpty(name)
               && IsIdentifierStart(name[0])
               && name.Skip(1).All(IsIdentifierContinue);
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c is '_' or '$';

    private static bool IsIdentifierContinue(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';

    public static IEnumerable<string> FilterInvalidVariableNames(this IEnumerable<string> names)
    {
        return names.Where(IsValidVariableName);
    }
}