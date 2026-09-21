namespace Elsa.Primitives.Identity;

/// <summary>
/// Encodes 64-bit values as fixed-width, lexicographically sortable Base62 strings.
/// </summary>
/// <remarks>
/// The alphabet is ordered by ascending ASCII code point (<c>0-9</c>, then <c>A-Z</c>, then <c>a-z</c>) and the
/// output is always 11 characters wide. Fixed width plus an ascending alphabet guarantees that ordinal string
/// comparison of two encoded values matches numeric comparison of the original values, which is what keeps
/// time-ordered identifiers sortable once stored as strings. 11 characters is the smallest width that can hold
/// the full <see cref="ulong"/> range (62^11 &gt; 2^64).
/// </remarks>
internal static class Base62
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int Width = 11;

    public static string Encode(ulong value)
    {
        Span<char> buffer = stackalloc char[Width];

        for (var i = Width - 1; i >= 0; i--)
        {
            buffer[i] = Alphabet[(int)(value % 62)];
            value /= 62;
        }

        return new string(buffer);
    }
}
