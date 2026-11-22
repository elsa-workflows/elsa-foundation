using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

public sealed partial class ActivityDataSelectionAnalyzerTests
{
    [Theory]
    [InlineData("unknown")]
    [InlineData("wrong-case")]
    [InlineData("ordinal-alias")]
    [InlineData("ambiguous")]
    [InlineData("intrinsic")]
    [InlineData("default-array")]
    [InlineData("null-entry")]
    [InlineData("duplicate-input")]
    [InlineData("empty-input-key")]
    [InlineData("children")]
    [InlineData("null-map")]
    [InlineData("too-many")]
    public async Task Malformed_compiler_boundary_facts_are_explicitly_refused(string kind)
    {
        using var provider = Provider();
        var root = Sequence(Node("consumer"), Node("producer"));
        var facts = ImmutableDictionary.Create<string, ImmutableArray<ArgumentState>>(StringComparer.Ordinal).Add("consumer", []);
        switch (kind)
        {
            case "unknown": facts = facts.Clear().Add("missing", []); break;
            case "wrong-case": facts = facts.Clear().WithComparers(StringComparer.OrdinalIgnoreCase).Add("CONSUMER", []); break;
            case "ordinal-alias": facts = ImmutableDictionary.Create<string, ImmutableArray<ArgumentState>>(System.Collections.Generic.ReferenceEqualityComparer.Instance)
                .Add(new string("consumer".ToCharArray()), []).Add(new string("consumer".ToCharArray()), []); break;
            case "ambiguous": root = Sequence(Node("consumer"), Node("consumer")); break;
            case "intrinsic": root = Node("consumer") with { Intrinsic = new(AuthoredWorkflowIntrinsicKind.Return, new("String")) }; break;
            case "default-array": facts = facts.SetItem("consumer", default); break;
            case "null-entry": facts = facts.SetItem("consumer", [null!]); break;
            case "duplicate-input": facts = facts.SetItem("consumer", [ArgumentState.Null("CustomerId"), ArgumentState.Null("CustomerId")]); break;
            case "empty-input-key": facts = facts.SetItem("consumer", [ArgumentState.Null("")]); break;
            case "children": root = Sequence(Node("child")); facts = facts.Clear().Add(root.NodeId, []); break;
            case "null-map": facts = null!; break;
            case "too-many": facts = Enumerable.Range(0, 10001).ToImmutableDictionary(index => index.ToString(), _ => ImmutableArray<ArgumentState>.Empty); break;
        }
        var result = await Analyzer(provider).AnalyzeAsync(Document(root) with { CompiledBoundaryInputs = facts });
        Assert.Contains(result.Findings, finding => finding.Type == "ActivityData/InvalidCompiledBoundaryInputs");
    }

    [Theory]
    [InlineData("sequence")]
    [InlineData("flowchart")]
    public async Task An_empty_compiled_boundary_entry_makes_ignored_caller_structure_opaque_for_result_admission(string structure)
    {
        using var provider = Provider();
        var producer = Node("producer") with { Structure = new(structure == "sequence" ? "elsa.sequence.structure" : "elsa.flowchart.structure",
            "1.0.0", JsonSerializer.SerializeToElement(new { activities = Array.Empty<ActivityNode>(), connections = Array.Empty<object>() }, Web)) };
        var document = Document(Sequence(producer, Node("consumer", Read("direct", "producer"))));
        Assert.Contains((await Analyzer(provider).AnalyzeAsync(document)).Findings, finding => finding.Type == "ValueFlow/ScopeBoundary");
        var admitted = await Analyzer(provider).AnalyzeAsync(document with
        { CompiledBoundaryInputs = ImmutableDictionary<string, ImmutableArray<ArgumentState>>.Empty.Add("producer", []) });
        Assert.Empty(admitted.Findings);
        Assert.NotNull(producer.Structure);
    }

    [Fact]
    public async Task A_compiled_boundary_retains_ancestor_visibility_without_admitting_its_ignored_own_scope()
    {
        using var provider = Provider();
        var consumer = Node("consumer") with { Structure = new("elsa.sequence.structure", "1.0.0",
            JsonSerializer.SerializeToElement(new { activities = Array.Empty<ActivityNode>(), variables = new[] { Variable("private", "Private") } }, Web)) };
        var document = Document(Sequence(consumer)) with { Variables = [Variable("ancestor", "Ancestor")] };
        var selected = Input(new(new VariableReference("private", "consumer"), "Variable"));
        var rejected = await Analyzer(provider).AnalyzeAsync(document with
        { CompiledBoundaryInputs = ImmutableDictionary<string, ImmutableArray<ArgumentState>>.Empty.Add("consumer", [selected]) });
        Assert.Contains(rejected.Findings, finding => finding.Type == "Variable/UnresolvableReference");
        selected = Input(new(new VariableReference("ancestor", "workflow"), "Variable"));
        Assert.Empty((await Analyzer(provider).AnalyzeAsync(document with
        { CompiledBoundaryInputs = ImmutableDictionary<string, ImmutableArray<ArgumentState>>.Empty.Add("consumer", [selected]) })).Findings);
    }

    [Fact]
    public async Task Compiled_boundary_inputs_preserve_the_document_constructor_deconstruction_and_transport_shape()
    {
        var original = new ActivityDataSelectionDocument("WorkflowDefinition", "resource", "revision", null, [], [], []);
        var (kind, id, revision, root, inputs, variables, outputs) = original;
        Assert.Equal(("WorkflowDefinition", "resource", "revision"), (kind, id, revision));
        Assert.Null(root);
        Assert.Empty(inputs); Assert.Empty(variables); Assert.Empty(outputs);
        var server = original with { CompiledBoundaryInputs = original.CompiledBoundaryInputs.Add("consumer", [ArgumentState.Null("CustomerId")]) };
        var json = JsonSerializer.SerializeToElement(server, Web);
        Assert.False(json.TryGetProperty("compiledBoundaryInputs", out _));
        var supplied = JsonNode.Parse(json.GetRawText())!.AsObject();
        supplied["compiledBoundaryInputs"] = JsonNode.Parse("""{"consumer":[]} """);
        var restored = JsonSerializer.Deserialize<ActivityDataSelectionDocument>(supplied.ToJsonString(), Web)!;
        Assert.Empty(restored.CompiledBoundaryInputs);
        Assert.Equal(JsonSerializer.Serialize(original, Web), JsonSerializer.Serialize(restored, Web));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Compiled_boundary_inputs_preserve_explicit_unbound_and_null_values(bool nullArgument)
    {
        using var provider = Provider();
        var selected = ArgumentState.Null("CustomerId");
        if (nullArgument) selected = selected with { Value = null! };
        var document = Document(Node("consumer")) with
        { CompiledBoundaryInputs = ImmutableDictionary<string, ImmutableArray<ArgumentState>>.Empty.Add("consumer", [selected]) };
        var result = await Analyzer(provider).AnalyzeAsync(document);
        // A null payload is a valid explicit value; a missing ArgumentValue container retains
        // the existing malformed-source refusal rather than acquiring new binding semantics.
        if (nullArgument) Assert.Contains(result.Findings, finding => finding.Type == "ActivityData/InvalidSource");
        else Assert.Empty(result.Findings);
    }
}
