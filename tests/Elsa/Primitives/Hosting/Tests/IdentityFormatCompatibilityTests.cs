using System.Reflection;
using Elsa.Primitives.Identity;
using Xunit;

namespace Elsa.Primitives.Hosting.Tests;

/// <summary>
/// Golden-value tests that pin the on-the-wire id formats. These literals MUST stay identical to the
/// equivalent test in the Groundwork repo (tests/Groundwork/Groundwork.Tests/IdentityFormatCompatibilityTests.cs)
/// so ids produced by either codebase remain format-compatible. Do not change a literal here without
/// changing it there.
/// </summary>
public sealed class IdentityFormatCompatibilityTests
{
    private static readonly MethodInfo Base62Encode = Type
        .GetType("Elsa.Primitives.Identity.Base62, Elsa.Primitives")!
        .GetMethod("Encode", BindingFlags.Public | BindingFlags.Static)!;

    private static string Encode(ulong value) => (string)Base62Encode.Invoke(null, [value])!;

    [Theory]
    [InlineData(0UL, "00000000000")]
    [InlineData(1UL, "00000000001")]
    [InlineData(61UL, "0000000000z")]
    [InlineData(62UL, "00000000010")]
    [InlineData(1000UL, "000000000G8")]
    [InlineData(1UL << 22, "0000000Hb84")]
    [InlineData(1UL << 41, "0000ciKbTd2")]
    [InlineData(ulong.MaxValue, "LygHa16AHYF")]
    public void Base62GoldenVectors(ulong value, string expected) => Assert.Equal(expected, Encode(value));

    [Fact]
    public void SnowflakeGolden_Epoch2020_Instant2024_Worker1_Seq0()
    {
        // epoch 2020-01-01Z (default), clock at 2024-01-01Z, worker 1, first sequence => fixed string.
        var clock = new MutableClock(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var sequence = new SnowflakeIdentitySequence(new SnowflakeIdentityGeneratorOptions { WorkerId = 1 });
        var generator = new SnowflakeIdentityGenerator(clock, sequence);

        Assert.Equal("0d6salhsREG", generator.Generate());
    }

    [Fact]
    public void UuidV7Golden_TimestampPrefix()
    {
        var clock = new MutableClock(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var id = new UuidV7IdentityGenerator(clock).Generate();

        Assert.Equal(32, id.Length);
        Assert.Equal("018cc251f400", id[..12]); // 48-bit unix-ms timestamp prefix
    }
}
