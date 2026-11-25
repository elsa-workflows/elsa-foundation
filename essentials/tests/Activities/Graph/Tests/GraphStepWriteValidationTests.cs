using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Diagnostics;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Graph.Tests;

/// <summary>
/// Spec 1241 User Story 3 (T010): a reusable activity's body gets the step-write checks a workflow draft gets, through
/// <see cref="IStepVariableWriteAnalyzer"/>, in draft validation and in compilation. Its findings become graph diagnostics
/// at the node's location, and its step writers join the concurrent-write warning.
/// </summary>
public sealed partial class GraphActivityProviderTests
{
    private const string Parallel = """
        { "nodeId": "root", "activityVersionId": "activity-ver-root", "inputs": [], "outputs": [],
          "structure": { "kind": "elsa.parallel.structure", "schemaVersion": "1.0.0", "payload": { "branches": [
            { "name": "left", "activity": { "nodeId": "first", "activityVersionId": "activity-ver-step", "inputs": [], "outputs": [], "structure": null } },
            { "name": "right", "activity": { "nodeId": "second", "activityVersionId": "activity-ver-step", "inputs": [], "outputs": [], "structure": null } }
          ] } } }
        """;

    [Fact]
    public async Task Body_step_write_findings_become_graph_diagnostics_in_draft_validation_and_compilation()
    {
        var analyzer = new StubStepWriteAnalyzer(
            new StepVariableWriteFacts(new Dictionary<string, StepVariableWriters>(StringComparer.Ordinal)
            {
                ["first"] = new([new StepVariableWriteTarget("workflow", "running-total")], false),
                ["second"] = new([], true)
            }, ["running-total"]),
            [
                new ValidationError("first/inputs/Balance", StepVariableWriteRules.VariableReference, "Input 'Balance' of node 'first' is a variable reference and must be bound to a declared variable."),
                new ValidationError("second/outputs/result", StepVariableWriteRules.StepWriteOverlapsCapture, "Node 'second' may write variable 'running-total' in its step and also captures output 'result' into it.")
                    { Severity = ValidationSeverities.Warning },
                new ValidationError("second/inputs/script", StepVariableWriteRules.RefusedScriptWrite, "VF-WRITE-002: the script writes an undeclared variable.")
            ]);
        var provider = StepWriteProvider(analyzer);
        var manifest = TotalManifest(Parallel);

        var draft = await provider.ValidateAsync(manifest, TotalContract());
        var compilation = await provider.CompileAsync(TotalRequest(manifest, new("elsa.parallel.structure", "1.0.0")));

        foreach (var diagnostics in new[] { draft, compilation.Diagnostics })
        {
            var reference = Assert.Single(diagnostics, x => x.Code == "activity.graph.variable-reference");
            Assert.Equal(ActivityDiagnosticSeverity.Error, reference.Severity);
            Assert.Equal("/rootActivity/structure/payload/branches/0/activity", reference.Location!.JsonPointer);
            Assert.Equal("first/inputs/Balance", reference.Metadata["validationPath"]);
            var script = Assert.Single(diagnostics, x => x.Code == "activity.graph.script-write");
            Assert.Equal(ActivityDiagnosticSeverity.Error, script.Severity);
            var overlap = Assert.Single(diagnostics, x => x.Code == "activity.graph.step-write-overlaps-capture");
            Assert.Equal(ActivityDiagnosticSeverity.Warning, overlap.Severity);
            // US3 AS1, AS2: the step writers are counted; a node that may write any visible variable says so.
            var unordered = Assert.Single(diagnostics, x => x.Code == "activity.graph.unordered-variable-write");
            Assert.Contains("'first', 'second'", unordered.Message, StringComparison.Ordinal);
            Assert.Contains("may write any visible variable", unordered.Message, StringComparison.Ordinal);
        }

        // The body's own variables are the root variables the analyzer resolves the body against.
        Assert.All(analyzer.RootVariables, variables => Assert.Equal(["running-total"], variables.Select(x => x.ReferenceKey)));
        Assert.Empty(compilation.Occurrences);
    }

    [Fact]
    public async Task Without_an_analyzer_the_body_is_checked_for_captures_only()
    {
        var draft = await WriteOrderProvider.ValidateAsync(TotalManifest(Parallel), TotalContract());

        Assert.DoesNotContain(draft, x => x.Code is "activity.graph.variable-reference" or "activity.graph.unordered-variable-write");
    }

    private static GraphActivityProvider StepWriteProvider(IStepVariableWriteAnalyzer analyzer)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => analyzer);
        return new GraphActivityProvider(
            new TestActivityStructureService(),
            new ActivityContractAuthoringValidator(new TestContractCapabilityCatalog()),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            WriteOrderAnalyzer());
    }

    private sealed class StubStepWriteAnalyzer(StepVariableWriteFacts facts, IReadOnlyList<ValidationError> findings) : IStepVariableWriteAnalyzer
    {
        public List<IReadOnlyCollection<VariableDefinition>> RootVariables { get; } = [];

        public ValueTask<StepVariableWriteAnalysis> AnalyzeAsync(ActivityNode root, IReadOnlyCollection<VariableDefinition> rootVariables, CancellationToken cancellationToken = default)
        {
            RootVariables.Add(rootVariables);
            return ValueTask.FromResult(new StepVariableWriteAnalysis(facts, findings));
        }
    }
}
