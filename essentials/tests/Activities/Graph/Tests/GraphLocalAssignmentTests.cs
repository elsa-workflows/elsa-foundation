using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Xunit;

namespace Elsa.Activities.Graph.Tests;

public sealed partial class GraphActivityProviderTests
{
    [Theory]
    [InlineData("body", "local", false)]
    [InlineData("workflow", "local", true)]
    [InlineData("activity-graph", "local", true)]
    [InlineData("foreign", "local", true)]
    [InlineData("sibling", "local", true)]
    [InlineData("assign", "local", true)]
    [InlineData("body", "missing", true)]
    public async Task Local_set_requires_one_declared_native_ancestor_below_the_graph_boundary(string scope, string key, bool refused)
    {
        var provider = new GraphActivityProvider(new TestActivityStructureService
        {
            ScopedVariables = new Dictionary<string, IReadOnlyCollection<VariableDefinition>>
            {
                ["body"] = [new("local", "Local", new("Decimal"), null, new ArgumentValue(0m, "Literal"))],
                ["sibling"] = [new("local", "Sibling local", new("Decimal"), null, new ArgumentValue(0m, "Literal"))]
            }
        }, new ActivityContractAuthoringValidator(new TestContractCapabilityCatalog()));
        var payload = JsonNode.Parse("""
            {"rootActivity":{"nodeId":"body","activityVersionId":"sequence-version","inputs":[],"outputs":[],
              "structure":{"kind":"Sequence","schemaVersion":"1","payload":{"activities":[
                {"nodeId":"assign","activityVersionId":"elsa.intrinsic.set@1","inputs":[{"referenceKey":"value","value":{"expressionType":"Literal","value":3}}],"outputs":[],
                 "intrinsic":{"kind":"set","valueType":{"alias":"Elsa.Any","collectionKind":"Single"},"variable":{"referenceKey":"local","declaringScopeId":"body"}}},
                {"nodeId":"sibling","activityVersionId":"sequence-version","inputs":[],"outputs":[]}
              ]}}},"variables":[],"outputMappings":[]}
            """)!;
        var target = payload["rootActivity"]!["structure"]!["payload"]!["activities"]![0]!["intrinsic"]!["variable"]!;
        target["referenceKey"] = key; target["declaringScopeId"] = scope;
        var diagnostics = await provider.ValidateAsync(new(GraphActivityProvider.Key, "1", JsonSerializer.SerializeToElement(payload)), OutcomeContract(("done", "Done")));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "activity.graph.intrinsic-unsupported");
        Assert.Equal(refused, diagnostics.Any(diagnostic => diagnostic.Code == "activity.graph.set-local-target-invalid"));
        if (!refused) Assert.Empty(diagnostics);
    }
}
