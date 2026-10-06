namespace Elsa.Modularity.Planning.Bridge;

/// <summary>Shared basename grammar for complete Workbench JSON source inventories.</summary>
public static class CompositionSourceFiles
{
    public static bool IsEnvironmentName(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    public static bool IsSupportedName(string? name) => name is not null &&
        (name.Equals("shells.json", StringComparison.OrdinalIgnoreCase) ||
         name.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase) ||
         (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
          (name.StartsWith("shells.", StringComparison.OrdinalIgnoreCase) ||
           name.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase)) &&
          IsEnvironmentName(name[(name.IndexOf('.') + 1)..^5])));
}
