using Elsa.Studio.Preferences.Api.Services;
using Elsa.Studio.Preferences.Core.Exceptions;
using Elsa.Studio.Preferences.Core.Models;
using Xunit;

namespace Elsa.Studio.Preferences.Tests;

public sealed class StudioPreferencePreconditionTests
{
    [Fact]
    public void IfNoneMatchStarMeansCreateOnly()
    {
        var condition = StudioPreferencePreconditions.Parse(null, "*");
        Assert.Equal(StudioPreferenceWriteConditionKind.MustNotExist, condition.Kind);
    }

    [Fact]
    public void QuotedIfMatchMeansCasUpdate()
    {
        var condition = StudioPreferencePreconditions.Parse("\"rev-7\"", null);
        Assert.Equal(StudioPreferenceWriteConditionKind.RevisionMatches, condition.Kind);
        Assert.Equal("rev-7", condition.Revision);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("rev-1", null)]
    [InlineData("\"rev-1\"", "*")]
    public void MissingMalformedOrAmbiguousConditionsAreRejected(string? ifMatch, string? ifNoneMatch)
    {
        Assert.Throws<StudioPreferenceValidationException>(() => StudioPreferencePreconditions.Parse(ifMatch, ifNoneMatch));
    }
}
