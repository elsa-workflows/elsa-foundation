using Elsa.Activities.Design.Persistence.Core.Services;
using Xunit;
using Outcome = Elsa.Activities.Design.Persistence.Core.Services.ActivityDefinitionVersionRefresh.Outcome;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class ActivityDefinitionVersionRefreshTests
{
    [Theory]
    [InlineData("old", "old", "new", Outcome.Write)]
    [InlineData("new", "old", "new", Outcome.AlreadyWritten)]
    [InlineData("other", "old", "new", Outcome.Conflict)]
    [InlineData(null, "old", "new", Outcome.Conflict)]
    [InlineData("new", null, "new", Outcome.AlreadyWritten)]
    public void Classify_MatchesTheCompareAndSwapProtocol(
        string? stored,
        string? expectedOld,
        string? incoming,
        Outcome expected)
    {
        Assert.Equal(expected, ActivityDefinitionVersionRefresh.Classify(stored, expectedOld, incoming));
    }
}
