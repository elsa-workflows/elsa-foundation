using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Xunit;

namespace Elsa.Activities.Design.Tests;

public sealed class ActivityNodePresentationTests
{
    [Fact]
    public void Labels_are_normalized_without_changing_opaque_node_identity()
    {
        var record = new ActivityNodePresentationRecord(" node/opaque ", "  Approve order  ", " \nReview it.\n ");
        Assert.Equal(" node/opaque ", record.NodeId);
        Assert.Equal("Approve order", record.DisplayName);
        Assert.Equal("Review it.", record.Description);
        Assert.Empty(ActivityNodePresentationRecord.NormalizeCollection([new("empty", "  ", "\n")]));
        Assert.Throws<ArgumentException>(() => ActivityNodePresentationRecord.NormalizeCollection([record, record]));
        Assert.Throws<ArgumentException>(() => new ActivityNodePresentationRecord(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActivityNodePresentationRecord("node", new string('x', 201)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActivityNodePresentationRecord("node", description: new string('x', 2_001)));
    }

    [Fact]
    public void Older_sidecars_default_to_empty_and_typed_metadata_roundtrips()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Empty(JsonSerializer.Deserialize<ActivityDefinitionDraftLayout>("{\"draftId\":\"d\",\"records\":[]}", options)!.ActivityPresentation);
        Assert.Empty(JsonSerializer.Deserialize<ActivityDefinitionVersionLayout>("{\"definitionVersionId\":\"v\",\"records\":[]}", options)!.ActivityPresentation);
        var sidecar = new ActivityDefinitionVersionLayout { DefinitionVersionId = "v", ActivityPresentation = [new("node", "Review")] };
        var json = JsonSerializer.Serialize(sidecar, options);
        Assert.DoesNotContain("isEmpty", json);
        Assert.Equal("Review", Assert.Single(JsonSerializer.Deserialize<ActivityDefinitionVersionLayout>(json, options)!.ActivityPresentation).DisplayName);
    }

    [Fact]
    public void Presentation_review_hash_is_order_stable_and_changes_for_names_or_descriptions()
    {
        ActivityNodePresentationRecord[] records = [new("a", "First"), new("b", description: "Second")];
        var hash = ActivityNodePresentationHasher.Compute(records);
        Assert.Equal(hash, ActivityNodePresentationHasher.Compute(records.Reverse()));
        Assert.NotEqual(hash, ActivityNodePresentationHasher.Compute([new("a", "Renamed"), records[1]]));
        Assert.NotEqual(hash, ActivityNodePresentationHasher.Compute([records[0], new("b", description: "Changed")]));
        Assert.Equal(ActivityNodePresentationHasher.Compute(null), ActivityNodePresentationHasher.Compute([]));
    }
}
