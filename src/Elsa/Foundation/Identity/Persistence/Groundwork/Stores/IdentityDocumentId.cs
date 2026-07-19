using System.Text;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

public static class IdentityDocumentId
{
    public static string From(params string?[] parts)
    {
        var normalized = parts.Select(part => InvariantIgnoreCaseKey(part ?? string.Empty));
        return "identity:" + IdentityHashFraming.Sha256Hex(normalized);
    }

    private static string InvariantIgnoreCaseKey(string value)
    {
        var builder = new StringBuilder();
        foreach (var rune in value.Trim().EnumerateRunes())
        {
            if (rune.Value == 0x212A)
            {
                builder.Append('K');
                continue;
            }

            var (lower, upper) = IdentityUnicodeCaseCompatibility.GetInvariantCaseVariants(rune);
            builder.Append(string.CompareOrdinal(lower, upper) <= 0 ? lower : upper);
        }

        return builder.ToString();
    }
}
