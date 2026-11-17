using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

public sealed partial class ActivityDataSelectionAnalyzerTests
{
    /// <summary>
    /// A scope-free result binding names only the producer, so every structure that encloses the producer and its reader
    /// must state that no earlier run of the child holding both can lead to a later one. One that does not say, such as a
    /// structure from outside this repository or a Flowchart whose analysis cannot be built, is not trusted: the binding is
    /// not offered and a stored one is refused before publication writes anything.
    /// </summary>
    [Theory]
    [InlineData("silent-structure")]
    [InlineData("unproven-flowchart")]
    public async Task A_scope_free_result_binding_is_refused_when_an_enclosing_structure_does_not_say_whether_it_repeats(string enclosing)
    {
        using var provider = Provider(services => services.AddSingleton<IActivityStructureHandler>(new SilentStructureHandler()));
        ActivityNode Root(ActivityNode consumer)
        {
            var steps = Sequence(Node("producer"), consumer) with { NodeId = "steps" };
            return enclosing == "silent-structure"
                ? SilentStructureHandler.Around(steps)
                : Flowchart([steps], [], "not-a-child");
        }

        var browsed = await Analyzer(provider).AnalyzeAsync(Document(Root(Node("consumer"))), new("consumer", "CustomerId"));
        Assert.Empty(browsed.Findings);
        var source = Assert.Single(browsed.Sources, source => source.Id == "activity:producer:$result");
        Assert.Equal(ActivityChildResultAvailability.Definite, source.Availability);
        Assert.Null(source.Binding);
        Assert.Equal("EarlierProducerRun", source.ReasonCode);

        var stored = await Analyzer(provider).AnalyzeAsync(Document(Root(Node("consumer", Input(ScopeFreeResult("producer"))))));
        Assert.Equal(new[] { "ValueFlow/EarlierProducerRun" }, stored.Findings.Select(finding => finding.Type));
        Assert.Equal(new[] { "consumer/inputs/CustomerId" }, stored.Findings.Select(finding => finding.Path));

        // Only the scope-free direct form changes; a binding that declares a scope keeps today's rules.
        var scoped = await Analyzer(provider).AnalyzeAsync(Document(Root(Node("consumer", Input(Result("producer", scope: "steps"))))));
        Assert.Empty(scoped.Findings);
    }

    /// <summary>
    /// Enclosing structures that state the fact for the child holding producer and reader keep the binding: a loop
    /// schedules every pass of its body itself, and a reader on a Flowchart cycle reading a producer that ran once before
    /// the Flowchart has one producer run behind it. The walk ends at the document's root, so a reusable activity's body
    /// is judged on its own graph whatever placement later repeats it.
    /// </summary>
    [Theory]
    [InlineData("flowchart-in-for")]
    [InlineData("sequence-in-while")]
    [InlineData("producer-before-cycling-flowchart")]
    [InlineData("reusable-activity-body")]
    public async Task A_scope_free_result_binding_is_offered_and_admitted_where_every_enclosing_structure_rules_out_an_earlier_run(string placement)
    {
        using var provider = Provider();
        ActivityNode Root(ActivityNode consumer) => placement switch
        {
            "flowchart-in-for" => Loop(Elsa.Activities.For.Activities.For.StructureKind,
                Flowchart([Node("producer"), consumer], [("producer", "consumer")], "producer")),
            "sequence-in-while" => Loop(Elsa.Activities.While.Activities.While.StructureKind, Sequence(Node("producer"), consumer) with { NodeId = "body" }),
            "producer-before-cycling-flowchart" => Sequence(Node("producer"),
                Flowchart([Node("entry"), consumer], [("entry", "consumer"), ("consumer", "entry")], "entry")),
            _ => Sequence(Node("producer"), consumer)
        };
        ActivityDataSelectionDocument Of(ActivityNode root) => placement == "reusable-activity-body"
            ? new("ActivityDefinition", "actual-activity", "exact-candidate-sha", root, [], [], [])
            : Document(root);

        var browsed = await Analyzer(provider).AnalyzeAsync(Of(Root(Node("consumer"))), new("consumer", "CustomerId"));
        Assert.Empty(browsed.Findings);
        var source = Assert.Single(browsed.Sources, source => source.Id == "activity:producer:$result");
        Assert.Equal(ActivityChildResultAvailability.Definite, source.Availability);
        Assert.Null(source.ReasonCode);
        var binding = Assert.IsType<JsonElement>(source.Binding!.Value);
        Assert.False(binding.TryGetProperty("producerScopeId", out _));

        var stored = await Analyzer(provider).AnalyzeAsync(Of(Root(Node("consumer",
            Input(new ArgumentValue(binding, "ActivityResult"))))));
        Assert.Empty(stored.Findings);
    }

    private static ActivityNode Loop(string kind, ActivityNode body) => new("loop", "test", [], [], new(kind, "1.0.0",
        JsonSerializer.SerializeToElement(new Elsa.Activities.ControlFlow.Loops.LoopAuthoredStructure(body), Web)));

    /// <summary>
    /// A structure handler from outside this repository that projects its children and states nothing about their
    /// results, so it inherits the interface defaults.
    /// </summary>
    private sealed class SilentStructureHandler : IActivityStructureHandler
    {
        public const string StructureKind = "test.silent.structure";

        public string Kind => StructureKind;
        public string SchemaVersion => "1.0.0";

        public static ActivityNode Around(ActivityNode child) => new("silent", "test", [], [],
            new(StructureKind, "1.0.0", JsonSerializer.SerializeToElement(new { activities = new[] { child } }, Web)));

        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) =>
            [new("activities", activity.Structure!.Payload.GetProperty("activities").Deserialize<ActivityNode[]>(Web)!)];

        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) =>
            throw new NotSupportedException();

        public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity) => throw new NotSupportedException();
    }
}
