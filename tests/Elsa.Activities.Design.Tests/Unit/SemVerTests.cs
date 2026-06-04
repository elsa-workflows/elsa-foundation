using Elsa.Primitives.Versioning;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public class SemVerTests
{
    [Theory]
    [InlineData("1.0.0", 1, 0, 0)]
    [InlineData("10.20.30", 10, 20, 30)]
    [InlineData("0.0.0", 0, 0, 0)]
    public void TryParse_ValidCore_ParsesSegments(string input, int major, int minor, int patch)
    {
        Assert.True(SemVer.TryParse(input, out var semVer));
        Assert.Equal(major, semVer.Major);
        Assert.Equal(minor, semVer.Minor);
        Assert.Equal(patch, semVer.Patch);
        Assert.Null(semVer.Prerelease);
        Assert.Null(semVer.BuildMetadata);
    }

    [Fact]
    public void TryParse_PrereleaseAndBuild_AreSeparated()
    {
        Assert.True(SemVer.TryParse("1.2.3-alpha.1+build.7", out var semVer));
        Assert.Equal(1, semVer.Major);
        Assert.Equal(2, semVer.Minor);
        Assert.Equal(3, semVer.Patch);
        Assert.Equal("alpha.1", semVer.Prerelease);
        Assert.Equal("build.7", semVer.BuildMetadata);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("1.0.x")]
    [InlineData("01.0.0")]
    [InlineData("1.00.0")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-alpha..1")]
    [InlineData("1.0.0-alpha_beta")]
    [InlineData("1.0.0-01")]
    [InlineData("1.0.0+")]
    [InlineData("1.0.0+build..1")]
    public void TryParse_Invalid_ReturnsFalse(string? input)
    {
        Assert.False(SemVer.TryParse(input, out _));
    }

    [Theory]
    [InlineData("2.0.0", "10.0.0")]
    [InlineData("1.0.2", "1.0.10")]
    [InlineData("1.0.0", "1.1.0")]
    [InlineData("1.9.0", "1.10.0")]
    public void CompareTo_NumericCore_OrdersNumerically(string lower, string higher)
    {
        Assert.True(SemVer.TryParse(lower, out var a));
        Assert.True(SemVer.TryParse(higher, out var b));
        Assert.True(a < b);
        Assert.True(b > a);
    }

    [Fact]
    public void CompareTo_Prerelease_HasLowerPrecedenceThanRelease()
    {
        Assert.True(SemVer.TryParse("1.0.0-alpha", out var pre));
        Assert.True(SemVer.TryParse("1.0.0", out var release));
        Assert.True(pre < release);
    }

    [Theory]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-beta.11", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    public void CompareTo_SpecPrecedenceChain_Holds(string lower, string higher)
    {
        Assert.True(SemVer.TryParse(lower, out var a));
        Assert.True(SemVer.TryParse(higher, out var b));
        Assert.True(a < b, $"{lower} should be < {higher}");
    }

    [Fact]
    public void Equality_IgnoresBuildMetadata()
    {
        Assert.True(SemVer.TryParse("1.0.0", out var plain));
        Assert.True(SemVer.TryParse("1.0.0+build", out var withBuild));
        Assert.Equal(plain, withBuild);
        Assert.True(plain == withBuild);
        Assert.Equal(plain.GetHashCode(), withBuild.GetHashCode());
    }

    [Fact]
    public void Inequality_OnDifferentPrerelease()
    {
        Assert.True(SemVer.TryParse("1.0.0-alpha", out var a));
        Assert.True(SemVer.TryParse("1.0.0-beta", out var b));
        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void ToSortKey_OrdinalOrdering_EqualsPrecedence()
    {
        var versions = new[]
        {
            "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta",
            "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0",
            "1.0.10", "1.2.0", "2.0.0", "10.0.0",
        };

        for (var i = 0; i < versions.Length - 1; i++)
        {
            var lowerKey = SemVer.ToSortKey(versions[i]);
            var higherKey = SemVer.ToSortKey(versions[i + 1]);
            Assert.True(
                string.CompareOrdinal(lowerKey, higherKey) < 0,
                $"sort key of {versions[i]} should sort before {versions[i + 1]}");
        }
    }

    [Fact]
    public void ToSortKey_StaticOverload_ThrowsOnInvalid()
    {
        Assert.Throws<ArgumentException>(() => SemVer.ToSortKey("not-a-version"));
    }

    [Fact]
    public void SemVerComparer_DelegatesToCompareTo()
    {
        Assert.True(SemVer.TryParse("1.0.0", out var a));
        Assert.True(SemVer.TryParse("2.0.0", out var b));
        Assert.True(SemVerComparer.Instance.Compare(a, b) < 0);
        Assert.True(SemVerComparer.Instance.Compare(b, a) > 0);
        Assert.Equal(0, SemVerComparer.Instance.Compare(a, a));
    }

    [Fact]
    public void ToString_RoundTripsFullVersion()
    {
        Assert.True(SemVer.TryParse("1.2.3-alpha.1+build.7", out var semVer));
        Assert.Equal("1.2.3-alpha.1+build.7", semVer.ToString());
    }
}
