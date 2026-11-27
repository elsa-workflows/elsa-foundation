using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class ActivityTemplateMemberReadHashTests
{
    [Theory]
    [InlineData("consumer")]
    [InlineData("producer")]
    [InlineData("projection")]
    [InlineData("optional")]
    [InlineData("segments")]
    [InlineData("type")]
    [InlineData("collection")]
    [InlineData("presence")]
    [InlineData("nullability")]
    [InlineData("representation")]
    [InlineData("profile")]
    public void Every_occurrence_read_contract_field_changes_the_immutable_template_hash(string variant)
    {
        Assert.NotEqual(Hash("baseline"), Hash(variant));
        Assert.Equal(Hash(variant), Hash(variant));
    }

    private static string Hash(string variant)
    {
        var selection = new RuntimeReadSelection(1, variant == "segments" ? ["a", "b", ""] : ["a.b", ""], RuntimeReadSelectionMode.Known,
            new(variant == "type" ? "Int32" : "String", variant == "collection" ? CollectionKind.Array : CollectionKind.Single),
            variant == "presence", variant == "nullability", variant == "representation" ? ValueRepresentation.StructuredValue : ValueRepresentation.TextValue,
            variant == "profile" ? "elsa.activity-value-json/2" : "elsa.activity-value-json/1");
        var read = new ExecutableActivityTemplateMemberRead(new(variant == "producer" ? "other" : "producer", variant == "projection" ? "other" : "$result", null,
            variant == "optional"), selection);
        var overlay = new ExecutableActivityTemplateOccurrenceOverlay("2", JsonSerializer.SerializeToElement(Array.Empty<object>()), null, null,
            new Dictionary<string, ExecutableActivityTemplateMemberRead> { [variant == "consumer" ? "other" : "value"] = read });
        var root = new ExecutableNode("root", "root", "test", "1", new RuntimeActivityDescriptor("test", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>());
        return ExecutableActivityTemplateBehaviorHasher.Compute(root, new Dictionary<string, WorkflowExecutableResumeTarget>(),
            [new("definition", "version", "1", "child-template", "child-hash", "reader", ActivityInvocationOrigin.Empty, OccurrenceOverlay: overlay)],
            [], [], [], "provider", new Dictionary<string, string>());
    }
}
