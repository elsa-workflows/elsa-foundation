using Elsa.Primitives.Versioning;
using Xunit;

namespace Elsa.Primitives.Tests.Versioning;

public class SemVerComparerTests
{
    [Fact]
    public void SemVerComparer_DelegatesToCompareTo()
    {
        Assert.True(SemVer.TryParse("1.0.0", out var a));
        Assert.True(SemVer.TryParse("2.0.0", out var b));
        Assert.True(SemVerComparer.Instance.Compare(a, b) < 0);
        Assert.True(SemVerComparer.Instance.Compare(b, a) > 0);
        Assert.Equal(0, SemVerComparer.Instance.Compare(a, a));
    }
}
