using System.Text.Json;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Xunit;

namespace Elsa.Activities.Design.Api.Tests;

public sealed class ActivityAuthoringCatalogIconTests
{
    [Fact]
    public async Task Catalog_exposes_a_valid_icon_facet_as_iconName()
    {
        var view = await ListAsync(ActivityIconName.ToFacet("Rocket")!);

        var descriptor = Assert.Single(view.Activities, activity => activity.ActivityTypeKey == "Acme.Activities.NotifySlack");
        Assert.Equal("Rocket", descriptor.IconName);
    }

    [Fact]
    public async Task Catalog_omits_iconName_when_no_icon_facet_is_present()
    {
        var view = await ListAsync();

        var descriptor = Assert.Single(view.Activities, activity => activity.ActivityTypeKey == "Acme.Activities.NotifySlack");
        Assert.Null(descriptor.IconName);
    }

    [Fact]
    public async Task Catalog_drops_an_unsafe_icon_facet_instead_of_echoing_it()
    {
        var unsafeFacet = new ActivityDesignFacet(
            ActivityIconName.FacetKind,
            ActivityIconName.FacetSchemaVersion,
            JsonSerializer.SerializeToElement(new { iconName = "asset:../secrets/key.svg" }));

        var view = await ListAsync(unsafeFacet);

        var descriptor = Assert.Single(view.Activities, activity => activity.ActivityTypeKey == "Acme.Activities.NotifySlack");
        Assert.Null(descriptor.IconName);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("2", false)]
    public async Task Catalog_projects_only_supported_palette_hint_versions(string schemaVersion, bool supported)
    {
        var view = await ListAsync(new ActivityDesignFacet("elsa.activity-palette", schemaVersion,
            JsonSerializer.SerializeToElement(new { containerModes = new[] { "bpmn" } })));
        var descriptor = Assert.Single(view.Activities, item => item.ActivityTypeKey == "Acme.Activities.NotifySlack");
        Assert.Equal(supported, descriptor.PaletteHints.HasValue);
        if (supported) Assert.Equal("bpmn", descriptor.PaletteHints!.Value.GetProperty("containerModes")[0].GetString());
    }

    private static Task<ActivityAuthoringCatalogView> ListAsync(ActivityDesignFacet? iconFacet = null)
    {
        var definition = new ActivityDefinition
        {
            Id = "def-slack",
            ActivityTypeKey = "Acme.Activities.NotifySlack",
            Category = "Extensions",
            DisplayName = "Notify Slack",
        };
        var version = new ActivityDefinitionVersion("1.0.0", definition.Id)
        {
            Id = "ver-slack",
            ConsumerKey = "elsa.clr-activity",
            SourceKind = "CLR",
            SourceId = "Acme.Activities.dll",
            DesignFacets = iconFacet is null ? [] : [iconFacet]
        };

        return CatalogReaderFixture.ListAsync([definition], [version], new NoFeatureAttribution());
    }

    private sealed class NoFeatureAttribution : Elsa.Activities.Design.Api.Contracts.IActivityFeatureAttributionResolver
    {
        public ValueTask<string?> ResolveProvidingFeatureAsync(string activityTypeKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);
    }
}
