using System.Reflection;
using Xunit;

namespace Elsa.Primitives.Hosting.Tests;

public sealed class Base62Tests
{
    // Base62 is internal; invoke via reflection so the test does not force the type public.
    private static readonly MethodInfo EncodeMethod = Type
        .GetType("Elsa.Primitives.Identity.Base62, Elsa.Primitives")!
        .GetMethod("Encode", BindingFlags.Public | BindingFlags.Static)!;

    private static string Encode(ulong value) => (string)EncodeMethod.Invoke(null, [value])!;

    [Fact]
    public void EncodesToFixedWidthOfElevenCharacters()
    {
        Assert.Equal(11, Encode(0).Length);
        Assert.Equal(11, Encode(ulong.MaxValue).Length);
    }

    [Theory]
    [InlineData(0ul)]
    [InlineData(1ul)]
    [InlineData(61ul)]
    [InlineData(62ul)]
    [InlineData(ulong.MaxValue)]
    public void UsesOnlyAlphanumericCharacters(ulong value)
    {
        Assert.All(Encode(value), c => Assert.True(char.IsAsciiLetterOrDigit(c)));
    }

    [Fact]
    public void OrdinalStringOrderMatchesNumericOrder()
    {
        ulong[] values = [0, 1, 61, 62, 1_000, 1_000_000, long.MaxValue, ulong.MaxValue];

        for (var i = 1; i < values.Length; i++)
            Assert.True(string.CompareOrdinal(Encode(values[i - 1]), Encode(values[i])) < 0,
                $"Encoding of {values[i - 1]} should sort before {values[i]}.");
    }
}
