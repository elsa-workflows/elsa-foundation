using System.Diagnostics.CodeAnalysis;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Canonical wire values for the presentation-only input <c>tier</c> on <see cref="InputDefinition"/>
/// and the authoring catalog. CLR authors set <c>[ActivityInput(Tier = InputTier.Optional)]</c>; JSON
/// catalogs and the descriptor/version API set the same strings on <see cref="InputDefinition.Tier"/>.
/// An unset or unknown value stays <see langword="null"/> so the client applies its own default.
/// </summary>
public static class ActivityInputTiers
{
    public const string Essential = "essential";
    public const string Optional = "optional";
    public const string Setting = "setting";

    /// <summary>
    /// Returns the canonical lowercase wire value when <paramref name="value"/> is one of the three
    /// tiers (comparison is ordinal-ignore-case); otherwise <see langword="false"/>.
    /// </summary>
    public static bool TryNormalize(string? value, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.Equals(Essential, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Essential;
            return true;
        }

        if (trimmed.Equals(Optional, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Optional;
            return true;
        }

        if (trimmed.Equals(Setting, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Setting;
            return true;
        }

        return false;
    }

    /// <summary>The canonical wire value, or <see langword="null"/> when unset or unknown.</summary>
    public static string? NormalizeOrNull(string? value) =>
        TryNormalize(value, out var normalized) ? normalized : null;
}
