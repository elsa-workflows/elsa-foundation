using Elsa.Activities.Design.Core.Models;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class ActivityIconNameTests
{
    [Theory]
    [InlineData("Rocket")]
    [InlineData("GitBranch")]
    [InlineData("git-branch")]
    [InlineData("file-json-2")]
    [InlineData("asset:Acme.Slack/icons/slack.svg")]
    [InlineData("asset:Elsa.Activities.Email/wwwroot/icons/mail.svg")]
    [InlineData("ext:slack")]
    [InlineData("ext:my-icon")]
    public void TryNormalize_AcceptsLucideAssetAndExtensionNames(string value)
    {
        Assert.True(ActivityIconName.TryNormalize(value, out var normalized));
        Assert.Equal(value, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("asset:")]
    [InlineData("asset:Acme.Slack")]
    [InlineData("asset:../secrets/key.svg")]
    [InlineData("asset:Acme.Slack/../icons/slack.svg")]
    [InlineData("asset:https://evil.example/icon.svg")]
    [InlineData("ext:")]
    [InlineData("ext:../x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("Not a name")]
    [InlineData("asset:Acme.Slack\\icons\\slack.svg")]
    public void TryNormalize_RejectsUnknownOrUnsafeValues(string? value)
    {
        Assert.False(ActivityIconName.TryNormalize(value, out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void ToFacet_And_ReadFromFacets_RoundTripAValidIcon()
    {
        var facet = ActivityIconName.ToFacet("  Rocket  ");

        Assert.NotNull(facet);
        Assert.Equal(ActivityIconName.FacetKind, facet!.Kind);
        Assert.Equal("Rocket", ActivityIconName.ReadFromFacets([facet]));
    }

    [Fact]
    public void ReadFromFacets_IgnoresAnUnsafePersistedPayload()
    {
        var facet = new ActivityDesignFacet(
            ActivityIconName.FacetKind,
            ActivityIconName.FacetSchemaVersion,
            System.Text.Json.JsonSerializer.SerializeToElement(new { iconName = "asset:../x.svg" }));

        Assert.Null(ActivityIconName.ReadFromFacets([facet]));
        Assert.Null(ActivityIconName.ToFacet("asset:../x.svg"));
    }
}
