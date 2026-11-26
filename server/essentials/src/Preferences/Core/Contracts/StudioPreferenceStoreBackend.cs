namespace Elsa.Studio.Preferences.Core.Contracts;

/// <summary>
/// Identifies the selected Studio Preferences persistence backend so competing providers fail
/// deterministically instead of silently replacing one another according to registration order.
/// </summary>
public sealed record StudioPreferenceStoreBackend(string Name)
{
    public static void EnsureCompatible(string? existing, string incoming)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incoming);
        if (existing is not null && !string.Equals(existing, incoming, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"IStudioPreferenceStore is already bound to '{existing}' and cannot also bind '{incoming}'. " +
                "Enable only one Studio Preferences persistence feature.");
        }
    }
}
