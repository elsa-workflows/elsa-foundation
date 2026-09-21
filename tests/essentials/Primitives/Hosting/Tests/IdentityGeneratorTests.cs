using Elsa.Primitives.Identity;
using Xunit;

namespace Elsa.Primitives.Hosting.Tests;

public sealed class IdentityGeneratorTests
{
    private readonly MutableClock _clock = new();

    [Fact]
    public void UuidV7ProducesThirtyTwoHexCharacters()
    {
        var id = new UuidV7IdentityGenerator(_clock).Generate();

        Assert.Equal(32, id.Length);
        Assert.All(id, c => Assert.True(char.IsAsciiHexDigitLower(c)));
    }

    [Fact]
    public void UuidV7SortsChronologicallyByString()
    {
        var generator = new UuidV7IdentityGenerator(_clock);

        var earlier = generator.Generate();
        _clock.Advance(TimeSpan.FromSeconds(1));
        var later = generator.Generate();

        Assert.True(string.CompareOrdinal(earlier, later) < 0);
    }

    [Fact]
    public void ShortProducesElevenBase62Characters()
    {
        var id = new ShortIdentityGenerator(_clock).Generate();

        Assert.Equal(11, id.Length);
        Assert.All(id, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
    }

    [Fact]
    public void ShortSortsChronologicallyByString()
    {
        var generator = new ShortIdentityGenerator(_clock);

        var earlier = generator.Generate();
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        var later = generator.Generate();

        Assert.True(string.CompareOrdinal(earlier, later) < 0);
    }

    [Fact]
    public void ShortProducesDistinctValuesWithinSameMillisecond()
    {
        var generator = new ShortIdentityGenerator(_clock);
        var ids = Enumerable.Range(0, 1_000).Select(_ => generator.Generate()).ToList();

        // 22 random bits within a fixed ms: birthday collisions are possible but vanishingly unlikely at this volume.
        Assert.True(ids.Distinct().Count() > 990);
    }

    [Fact]
    public void SnowflakeIsMonotonicAndUniqueWithinSameMillisecond()
    {
        var sequence = new SnowflakeIdentitySequence(new() { WorkerId = 1 });
        var generator = new SnowflakeIdentityGenerator(_clock, sequence);

        var ids = Enumerable.Range(0, 4_096).Select(_ => generator.Generate()).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        var sorted = ids.OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, ids); // already strictly increasing
    }

    [Fact]
    public void SnowflakeDifferentWorkersProduceDifferentIds()
    {
        var a = new SnowflakeIdentityGenerator(_clock, new SnowflakeIdentitySequence(new() { WorkerId = 1 }));
        var b = new SnowflakeIdentityGenerator(_clock, new SnowflakeIdentitySequence(new() { WorkerId = 2 }));

        Assert.NotEqual(a.Generate(), b.Generate());
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(1024L)]
    public void SnowflakeRejectsWorkerIdOutOfRange(long workerId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnowflakeIdentitySequence(new() { WorkerId = workerId }));
    }

    [Fact]
    public void SnowflakeThrowsWhenClockMovesBackwards()
    {
        var sequence = new SnowflakeIdentitySequence(new() { WorkerId = 1 });
        var generator = new SnowflakeIdentityGenerator(_clock, sequence);

        generator.Generate();
        _clock.Advance(TimeSpan.FromMilliseconds(-10));

        Assert.Throws<InvalidOperationException>(() => generator.Generate());
    }

    [Fact]
    public void GuidProducesThirtyTwoHexCharacters()
    {
        var id = new GuidIdentityGenerator().Generate();

        Assert.Equal(32, id.Length);
        Assert.All(id, c => Assert.True(char.IsAsciiHexDigitLower(c)));
    }

    [Fact]
    public void ShortThrowsWhenClockIsBeforeEpoch()
    {
        var clock = new MutableClock(new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero)); // before the 2020-01-01Z epoch
        var generator = new ShortIdentityGenerator(clock);

        Assert.Throws<InvalidOperationException>(() => generator.Generate());
    }

    [Fact]
    public void SnowflakeThrowsWhenClockIsBeforeEpoch()
    {
        var clock = new MutableClock(new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero)); // before the 2020-01-01Z epoch
        var generator = new SnowflakeIdentityGenerator(clock, new SnowflakeIdentitySequence(new() { WorkerId = 1 }));

        Assert.Throws<InvalidOperationException>(() => generator.Generate());
    }
}
