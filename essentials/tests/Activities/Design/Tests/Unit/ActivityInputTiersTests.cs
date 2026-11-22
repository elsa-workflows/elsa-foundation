using Elsa.Activities.Design.Core.Models;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class ActivityInputTiersTests
{
    [Theory]
    [InlineData("essential", ActivityInputTiers.Essential)]
    [InlineData("Optional", ActivityInputTiers.Optional)]
    [InlineData(" SETTING ", ActivityInputTiers.Setting)]
    public void TryNormalize_AcceptsTheThreeTiers(string value, string expected)
    {
        Assert.True(ActivityInputTiers.TryNormalize(value, out var normalized));
        Assert.Equal(expected, normalized);
        Assert.Equal(expected, ActivityInputTiers.NormalizeOrNull(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("advanced")]
    [InlineData("essentials")]
    public void TryNormalize_RejectsUnsetAndUnknownValues(string? value)
    {
        Assert.False(ActivityInputTiers.TryNormalize(value, out var normalized));
        Assert.Null(normalized);
        Assert.Null(ActivityInputTiers.NormalizeOrNull(value));
    }

    [Fact]
    public void NormalizeOrNull_DropsAnUnknownStoredValue()
    {
        Assert.Null(ActivityInputTiers.NormalizeOrNull("advanced"));
        Assert.Null(ActivityInputTiers.NormalizeOrNull("1"));
    }
}
