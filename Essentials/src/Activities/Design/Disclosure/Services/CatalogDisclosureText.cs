namespace Elsa.Activities.Design.Disclosure.Services;

/// <summary>Validates opaque text without trimming, normalization or lossy surrogate replacement.</summary>
internal static class CatalogDisclosureText
{
    public static bool IsValid(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength && IsWellFormedUtf16(value);

    public static bool IsWellFormedUtf16(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (++index == value.Length || !char.IsLowSurrogate(value[index]))
                    return false;
            }
            else if (char.IsLowSurrogate(value[index]))
                return false;
        }
        return true;
    }
}
