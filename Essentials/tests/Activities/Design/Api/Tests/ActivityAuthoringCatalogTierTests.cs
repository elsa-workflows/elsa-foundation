using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Design.Api.Tests;

public sealed class ActivityAuthoringCatalogTierTests
{
    [Fact]
    public async Task Catalog_exposes_a_declared_tier_and_leaves_unset_null()
    {
        var view = await ListAsync(
            Input("url") with { Tier = ActivityInputTiers.Essential },
            Input("timeout") with { Tier = ActivityInputTiers.Optional },
            Input("authorize") with { Tier = ActivityInputTiers.Setting },
            Input("label"));

        var descriptor = Assert.Single(view.Activities, activity => activity.ActivityTypeKey == "Acme.Activities.CallService");
        var inputs = descriptor.Inputs.ToDictionary(input => input.ReferenceKey, StringComparer.Ordinal);

        Assert.Equal(ActivityInputTiers.Essential, inputs["url"].Tier);
        Assert.Equal(ActivityInputTiers.Optional, inputs["timeout"].Tier);
        Assert.Equal(ActivityInputTiers.Setting, inputs["authorize"].Tier);
        Assert.Null(inputs["label"].Tier);
    }

    [Fact]
    public async Task Catalog_drops_an_unknown_tier_instead_of_echoing_it()
    {
        var view = await ListAsync(Input("url") with { Tier = "advanced" });

        var descriptor = Assert.Single(view.Activities, activity => activity.ActivityTypeKey == "Acme.Activities.CallService");
        Assert.Null(Assert.Single(descriptor.Inputs).Tier);
    }

    [Fact]
    public async Task Catalog_wire_leaves_unset_tier_nullish_and_writes_a_declared_tier()
    {
        var view = await ListAsync(Input("label"), Input("timeout") with { Tier = ActivityInputTiers.Optional });
        var json = JsonNode.Parse(JsonSerializer.Serialize(view, ActivitiesDesignWire.Context.GetTypeInfo(typeof(ActivityAuthoringCatalogView))!))!;
        var inputs = json["activities"]!.AsArray().Single(activity => (string?)activity!["activityTypeKey"] == "Acme.Activities.CallService")!["inputs"]!.AsArray();

        var unset = inputs.Single(input => (string?)input!["referenceKey"] == "label")!["tier"];
        Assert.True(
            unset is null || unset is JsonValue { } value && value.GetValueKind() == JsonValueKind.Null,
            "Unset tier must be omitted or JSON null so the client applies its own default.");
        Assert.Equal(ActivityInputTiers.Optional, (string?)inputs.Single(input => (string?)input!["referenceKey"] == "timeout")!["tier"]);
    }

    private static Task<ActivityAuthoringCatalogView> ListAsync(params InputDefinition[] inputs)
    {
        var definition = new ActivityDefinition
        {
            Id = "def-call",
            ActivityTypeKey = "Acme.Activities.CallService",
            Category = "Acme",
            DisplayName = "Call Service",
        };
        var version = new ActivityDefinitionVersion("1.0.0", definition.Id)
        {
            Id = "ver-call",
            ConsumerKey = "elsa.clr-activity",
            Inputs = inputs
        };

        return CatalogReaderFixture.ListAsync([definition], [version], new NoFeatureAttribution());
    }

    private static InputDefinition Input(string referenceKey) =>
        new(referenceKey, referenceKey, new TypeReference("String"), null, referenceKey, null, IsNullable: true);

    private sealed class NoFeatureAttribution : Elsa.Activities.Design.Api.Contracts.IActivityFeatureAttributionResolver
    {
        public ValueTask<string?> ResolveProvidingFeatureAsync(string activityTypeKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);
    }
}
