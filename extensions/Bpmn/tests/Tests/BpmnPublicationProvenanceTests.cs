using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnPublicationProvenanceTests
{
    [Fact]
    public void Authored_provenance_captures_opaque_layout_before_its_owner_can_dispose_or_mutate_it()
    {
        var presentation = new List<WorkflowExecutableActivityPresentationRecord> { new("root", "Review order") };
        BpmnModelPublicationProvenance source;
        using (var document = JsonDocument.Parse("{\"diagram\":{\"shapes\":{\"human\":{\"x\":10}}}}"))
        {
            var layout = new List<WorkflowExecutableLayoutRecord> { new("root", 10, 20, AdditionalProperties: document.RootElement) };
            source = new("version-1", "1.0.0", layout, presentation);
            layout.Clear();
            presentation.Clear();
        }
        Assert.Single(source.Layout);
        Assert.Equal(10, source.Layout[0].AdditionalProperties!.Value.GetProperty("diagram").GetProperty("shapes").GetProperty("human").GetProperty("x").GetInt32());
        Assert.Equal("Review order", Assert.Single(source.ActivityPresentation).DisplayName);
    }
}
