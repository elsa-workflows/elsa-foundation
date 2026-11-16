using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Primitives.Diagnostics;
using Elsa.Workflows.Publishing.Core.Models;
using Xunit;
using DesignActivityContract = Elsa.Activities.Design.Core.Models.ActivityContract;

namespace Elsa.Activities.Graph.Tests;

/// <summary>Spec 266, Story 3: unordered captures into a graph activity's own variables.</summary>
public sealed partial class GraphActivityProviderTests
{
    private const string Capture = """[{ "referenceKey": "total", "value": { "expressionType": "Variable", "value": { "referenceKey": "running-total", "declaringScopeId": "workflow" } } }]""";

    // Scenarios 1 and 2.
    [Fact]
    public async Task Unordered_body_captures_into_one_graph_variable_warn_in_draft_validation_and_compilation()
    {
        var root = """
            { "nodeId": "root", "activityVersionId": "activity-ver-root", "inputs": [], "outputs": [],
              "structure": { "kind": "elsa.parallel.structure", "schemaVersion": "1.0.0", "payload": { "branches": [
                { "name": "left", "activity": { "nodeId": "first", "activityVersionId": "activity-ver-step", "inputs": [], "outputs": CAPTURE, "structure": null } },
                { "name": "right", "activity": { "nodeId": "second", "activityVersionId": "activity-ver-step", "inputs": [], "outputs": CAPTURE, "structure": null } }
              ] } } }
            """.Replace("CAPTURE", Capture, StringComparison.Ordinal);
        var manifest = TotalManifest(root);

        var draft = await _provider.ValidateAsync(manifest, TotalContract());
        var compilation = await _provider.CompileAsync(TotalRequest(manifest, new("elsa.parallel.structure", "1.0.0")));

        foreach (var diagnostics in new[] { draft, compilation.Diagnostics })
        {
            var warning = Assert.Single(diagnostics, x => x.Code == "activity.graph.unordered-variable-write");
            Assert.Equal(ActivityDiagnosticSeverity.Warning, warning.Severity);
            Assert.Equal("ValueFlow/ConcurrentWrite", warning.Metadata["validationType"]);
            Assert.Contains("'running-total'", warning.Message, StringComparison.Ordinal);
            Assert.Contains("'first', 'second'", warning.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(diagnostics, x => x.Severity == ActivityDiagnosticSeverity.Error);
        }

        Assert.Equal(2, compilation.Occurrences.Count(x => x.OccurrenceId is "first" or "second"));
    }

    // Scenario 3; spec 265's sequential-capture test keeps passing unchanged.
    [Fact]
    public async Task Sequential_body_captures_into_one_graph_variable_do_not_warn()
    {
        var root = """
            { "nodeId": "root", "activityVersionId": "activity-ver-root", "inputs": [], "outputs": [],
              "structure": { "kind": "Sequence", "schemaVersion": "1", "payload": { "activities": [
                { "nodeId": "first", "activityVersionId": "activity-ver-step", "inputs": [], "outputs": CAPTURE, "structure": null },
                { "nodeId": "second", "activityVersionId": "activity-ver-step", "inputs": [], "outputs": CAPTURE, "structure": null }
              ] } } }
            """.Replace("CAPTURE", Capture, StringComparison.Ordinal);
        var manifest = TotalManifest(root);

        var draft = await _provider.ValidateAsync(manifest, TotalContract());
        var compilation = await _provider.CompileAsync(TotalRequest(manifest, new("Sequence", "1")));

        Assert.DoesNotContain(draft, x => x.Code == "activity.graph.unordered-variable-write");
        Assert.DoesNotContain(compilation.Diagnostics, x => x.Code == "activity.graph.unordered-variable-write");
    }

    private static ActivityProviderManifest TotalManifest(string root) => new(
        GraphActivityProvider.Key,
        Design.Models.ActivityGraphManifest.SchemaVersion,
        JsonDocument.Parse($$"""
            {
              "variables": [{ "referenceKey": "running-total", "name": "RunningTotal", "type": { "alias": "Int64", "collectionKind": "Single" }, "storageDriverKey": "elsa.json" }],
              "rootActivity": {{root}},
              "outputMappings": []
            }
            """).RootElement.Clone());

    private static DesignActivityContract TotalContract() => new("1", [], [], [new ActivityOutcomeContract("done", "Done", true)]);

    private static ActivityTemplateCompilationRequest TotalRequest(ActivityProviderManifest manifest, ActivityStructureContract rootStructure) => new(
        "definition-1", "activity-type-1", "draft-1", 1, "1.0.0", TotalContract(), manifest,
        [
            CreateDependency("root", "activity-ver-root", "root-origin", OutcomeContract(("done", "Done")), declaredStructure: rootStructure),
            CreateDependency("first", "activity-ver-step", "first-origin"),
            CreateDependency("second", "activity-ver-step", "second-origin")
        ],
        "provider-fingerprint");
}
