using Elsa.Activities.Design.Core.Reconciliation;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// Pins the activity catalog stable id formula. Persisted catalogs and generated activity call facades
/// already hold these ids, so a failure here means the formula changed and stored references would dangle.
/// </summary>
public sealed class ActivityCatalogStableIdsTests
{
    [Theory]
    [InlineData("Elsa.Activities.Primitives.WriteLine", "1.0.0", "actver_8snkC6AmFiyn9vhwUA93wnAkS138G_1LbjN-pjz3Z4w")]
    [InlineData("Elsa.Activities.Http.SendHttpRequest", "2.3.4", "actver_np0zlCUZB29eSvfPEJERTmvHCV5-FUHgtkPZ5lKtGf4")]
    [InlineData("Acme.Activities.Approve", "1.0.0-beta.2", "actver_impkMynzN-WD_TJ736g75Vz9r7naqc73gEGtByTqV_4")]
    [InlineData("Acme.Activities.Approve", "1.0.0", "actver_yPTxZKhcz9-VEraBO-yQOAOt83RhFhmSPFvPPzYTnNU")]
    [InlineData("Acme.Activities.Approve", "1.0.0+sha.abc", "actver_yPTxZKhcz9-VEraBO-yQOAOt83RhFhmSPFvPPzYTnNU")]
    public void VersionId_IsPinned(string activityTypeKey, string version, string expected) =>
        Assert.Equal(expected, ActivityCatalogStableIds.VersionId(activityTypeKey, version));

    [Theory]
    [InlineData("Elsa.Activities.Primitives.WriteLine", "actdef_1z-i4uMGwHP5orrzu3RNhgZHuulhvuYynQlQ-meX81E")]
    [InlineData("Acme.Activities.Approve", "actdef_v0y0sX_oZsyuBOIVYBwGB3eKFepbiZ9hE0cc_opcGN8")]
    public void DefinitionId_IsPinned(string activityTypeKey, string expected) =>
        Assert.Equal(expected, ActivityCatalogStableIds.DefinitionId(activityTypeKey));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankActivityTypeKey_HasNoId(string activityTypeKey)
    {
        Assert.Null(ActivityCatalogStableIds.DefinitionId(activityTypeKey));
        Assert.Null(ActivityCatalogStableIds.VersionId(activityTypeKey, "1.0.0"));
    }

    [Fact]
    public void InvalidVersion_IsRejected() =>
        Assert.Throws<ArgumentException>(() => ActivityCatalogStableIds.VersionId("Acme.Activities.Approve", "1.0"));
}
