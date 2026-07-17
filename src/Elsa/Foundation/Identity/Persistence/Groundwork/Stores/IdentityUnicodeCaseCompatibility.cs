using System.Text;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

// Garay casing was added in Unicode 16.0. Keep durable Identity keys stable when the host ICU
// predates that data; all other mappings continue to use the runtime invariant tables.
internal static class IdentityUnicodeCaseCompatibility
{
    private const int GarayCapitalLetterStart = 0x10D50;
    private const int GarayCapitalLetterEnd = 0x10D65;
    private const int GaraySmallLetterStart = 0x10D70;
    private const int GaraySmallLetterEnd = 0x10D85;
    private const int GarayCaseOffset = 0x20;

    public static string ToLowerInvariant(string value)
    {
        var normalized = value.ToLowerInvariant();
        if (!ContainsGarayCapital(normalized))
            return normalized;

        var result = new StringBuilder(normalized.Length);
        for (var index = 0; index < normalized.Length; index++)
        {
            if (!TryGetGarayCapital(normalized, index, out var capital))
            {
                result.Append(normalized[index]);
                continue;
            }

            result.Append(char.ConvertFromUtf32(capital + GarayCaseOffset));
            index++;
        }

        return result.ToString();
    }

    public static (string Lower, string Upper) GetInvariantCaseVariants(Rune rune)
    {
        var valueText = rune.ToString();
        var lower = rune.Value is >= GarayCapitalLetterStart and <= GarayCapitalLetterEnd
            ? new Rune(rune.Value + GarayCaseOffset).ToString()
            : valueText.ToLowerInvariant();
        var upper = rune.Value is >= GaraySmallLetterStart and <= GaraySmallLetterEnd
            ? new Rune(rune.Value - GarayCaseOffset).ToString()
            : valueText.ToUpperInvariant();
        return (lower, upper);
    }

    private static bool ContainsGarayCapital(string value)
    {
        for (var index = 0; index < value.Length - 1; index++)
        {
            if (TryGetGarayCapital(value, index, out _))
                return true;
        }

        return false;
    }

    private static bool TryGetGarayCapital(string value, int index, out int capital)
    {
        capital = 0;
        if (index + 1 >= value.Length || !char.IsSurrogatePair(value[index], value[index + 1]))
            return false;

        var scalar = char.ConvertToUtf32(value[index], value[index + 1]);
        if (scalar is < GarayCapitalLetterStart or > GarayCapitalLetterEnd)
            return false;

        capital = scalar;
        return true;
    }
}
