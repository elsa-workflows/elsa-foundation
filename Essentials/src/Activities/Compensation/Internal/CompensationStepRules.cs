namespace Elsa.Activities.Compensation.Internal;

/// <summary>
/// The step-name rule shared by the Draft validator, the compile-time guard and the runtime navigator: a step name
/// is required and unique within its scope, compared ordinally after trimming.
/// </summary>
internal static class CompensationStepRules
{
    public static string Normalize(string? name) => name?.Trim() ?? string.Empty;

    public static IEnumerable<string> DuplicateNames(IEnumerable<string?> names) =>
        names
            .Select(Normalize)
            .Where(name => name.Length > 0)
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

    public static bool HasBlankName(IEnumerable<string?> names) => names.Any(name => Normalize(name).Length == 0);
}
