namespace Groundwork.Core.Validation;

public static class ProviderNeutralityRules
{
    private static readonly string[] ProviderSpecificFragments =
    [
        "table:",
        "column:",
        "collection:",
        "sql:",
        "mongodb:",
        "sqlite:",
        "postgres:",
        "postgresql:",
        "sqlserver:"
    ];

    public static bool LooksProviderSpecific(string value) =>
        ProviderSpecificFragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
