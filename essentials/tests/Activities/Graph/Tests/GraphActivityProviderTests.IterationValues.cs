using System.Text.Json;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Xunit;

namespace Elsa.Activities.Graph.Tests;

/// <summary>
/// #5849: a reusable body's loop occurrence carries the keys of its iteration values in its compiled structure, so the
/// caller's placed-tree read admission can see them. Publishing owns the field: an owner's own value is replaced.
/// </summary>
public sealed partial class GraphActivityProviderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_body_occurrence_carries_exactly_its_projected_iteration_values(bool declaresIndex)
    {
        var provider = new GraphActivityProvider(
            new TestActivityStructureService
            {
                IterationVariables = new Dictionary<string, IReadOnlyCollection<VariableDefinition>>
                {
                    ["branch"] = declaresIndex ? [new("index", "index", new TypeReference("Any"), null, null)] : []
                }
            },
            new ActivityContractAuthoringValidator(new TestContractCapabilityCatalog()));
        // The owner's structure handler forges the Publishing-owned field; compilation replaces it.
        var nested = $$"""
            {
              "nodeId": "branch",
              "activityVersionId": "activity-ver-if",
              "inputs": [],
              "outputs": [],
              "structure": { "kind": "If", "schemaVersion": "1", "payload": { "then": {{CompleteNodeJson("exit", "fail")}}, "else": null, "iterationValues": ["forged"] } }
            }
            """;
        var request = CompleteRequest(
            CompleteManifest(nested),
            extraDependencies: [CreateDependency("branch", "activity-ver-if", "branch-origin", declaredStructure: new("If", "1"))]);

        var compilation = await provider.CompileAsync(request);

        Assert.Empty(compilation.Diagnostics);
        var payload = Assert.Single(compilation.Occurrences, occurrence => occurrence.OccurrenceId == "branch").Structure!.Payload;
        if (declaresIndex)
            Assert.Equal(new[] { "index" }, payload.GetProperty(CompiledStructureFields.IterationValues).Deserialize<string[]>());
        else
            Assert.False(payload.TryGetProperty(CompiledStructureFields.IterationValues, out _));
    }
}
