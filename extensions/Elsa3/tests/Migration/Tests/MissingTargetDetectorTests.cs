using System.Text;
using Elsa3.Migration.Tooling;
using Elsa3.Models;

namespace Elsa3.Migration.Tests;

public sealed class MissingTargetDetectorTests
{
    [Fact]
    public void Dispatch_and_execute_targets_absent_from_the_export_are_listed()
    {
        var app = Parse(ExportInputTests.Definition("app", 1, rootActivities:
            """
            {"id":"d1","nodeId":"d1","type":"Elsa.DispatchWorkflow","workflowDefinitionId":{"typeName":"String","expression":{"type":"Literal","value":"absent"}}},
            {"id":"d2","nodeId":"d2","type":"Elsa.ExecuteWorkflow","workflowDefinitionId":"present"},
            {"id":"d3","nodeId":"d3","type":"Elsa.DispatchWorkflow","workflowDefinitionId":{"typeName":"String","expression":{"type":"JavaScript","value":"computed SENTINEL-1237"}}}
            """));
        var other = Parse(ExportInputTests.Definition("other", 1, rootActivities:
            """{"id":"d","nodeId":"d","type":"Elsa.BulkDispatchWorkflows","workflowDefinitionId":"absent"}"""));
        var present = Parse(ExportInputTests.Definition("present", 1));

        var missing = MissingTargetDetector.Detect([app, other], [app, other, present]);

        var target = Assert.Single(missing);
        Assert.Equal("absent", target.DefinitionId);
        Assert.Equal(["app", "other"], target.ReferencedBy);
    }

    private static Elsa3WorkflowDefinition Parse(string json) =>
        ExportInputReader.Parse(Encoding.UTF8.GetBytes(json), out _)!.Single();
}
