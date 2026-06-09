namespace Elsa.Expressions.Core.Extensions;

public static class VariableNameValidator
{
    public static bool IsValidVariableName(this string? name)
    {
        return !string.IsNullOrWhiteSpace(name) && name.All(char.IsLetterOrDigit);
    }

    public static IEnumerable<string> FilterInvalidVariableNames(this IEnumerable<string> names)
    {
        return names.Where(IsValidVariableName);
    }
}