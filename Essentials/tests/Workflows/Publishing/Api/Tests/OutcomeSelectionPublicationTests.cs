using System.Text.Json;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scripting.Activities;
using Elsa.Activities.Sequence;
using Elsa.Activities.Switch.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using IfActivity = Elsa.Activities.If.Activities.If;
using SwitchActivity = Elsa.Activities.Switch.Activities.Switch;
using static Elsa.Workflows.Publishing.Api.Tests.SecretBindingCompilerFixture;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Spec 1208 publication checks through the real executable compiler: a connection on a port outside its source's
/// pinned outcome set is refused (<c>VF-AUTH-011</c>, FR-019, FR-020), a node without <c>Done</c> must belong to an
/// activity that selects on every completion (<c>VF-AUTH-013</c>, FR-002b), and the pinned outcome set and
/// multi-outcome flag follow the Done rule (FR-002, FR-010 to FR-014, FR-031).
/// </summary>
public sealed class OutcomeSelectionPublicationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly IActivityStructureService Structures = StructureService();

    [Fact]
    public async Task A_connection_on_an_undeclared_port_is_refused_naming_the_connection_and_port()
    {
        var root = Flowchart(
            [Leaf("decide", typeof(AlwaysDecides)), Leaf("after", typeof(PlainStep))],
            [new(new("decide", "Maybe"), new("after"))]);

        var error = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => Compile(root, typeof(AlwaysDecides), typeof(PlainStep)));

        Assert.StartsWith("VF-AUTH-011:", error.Message, StringComparison.Ordinal);
        Assert.Contains("'flowchart'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'decide'", error.Message, StringComparison.Ordinal);
        Assert.Contains("port 'Maybe'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Approved'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Rejected'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_default_connection_from_a_node_without_Done_is_refused()
    {
        // An unnamed source port is Done; a node whose set has no Done never takes it.
        var root = Flowchart(
            [Leaf("decide", typeof(AlwaysDecides)), Leaf("after", typeof(PlainStep))],
            [new(new("decide"), new("after"))]);

        var error = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => Compile(root, typeof(AlwaysDecides), typeof(PlainStep)));

        Assert.StartsWith("VF-AUTH-011:", error.Message, StringComparison.Ordinal);
        Assert.Contains("port 'Done'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_declared_but_unconnected_outcome_publishes()
    {
        // FR-020: Rejected is declared and left unconnected; selecting it ends that path.
        var root = Flowchart(
            [Leaf("decide", typeof(AlwaysDecides)), Leaf("after", typeof(PlainStep))],
            [new(new("decide", "Approved"), new("after"))]);

        var executable = await Compile(root, typeof(AlwaysDecides), typeof(PlainStep));

        var decide = Assert.Single(executable.RootActivity.DescendantsAndSelf(), node => node.ExecutableNodeId == "decide");
        Assert.Equal(["Approved", "Rejected"], decide.ActivityContract!.Outcomes.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_node_without_Done_whose_activity_does_not_guarantee_a_selection_is_refused()
    {
        var error = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            Compile(Leaf("maybe", typeof(MayNotDecide)), typeof(MayNotDecide)));

        Assert.StartsWith("VF-AUTH-013:", error.Message, StringComparison.Ordinal);
        Assert.Contains("'maybe'", error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(MayNotDecide).FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_node_without_Done_whose_activity_always_selects_publishes()
    {
        var executable = await Compile(Leaf("decide", typeof(AlwaysDecides)), typeof(AlwaysDecides));

        Assert.DoesNotContain(ActivityOutcomes.Done, executable.RootActivity.ActivityContract!.Outcomes);
    }

    [Fact]
    public async Task If_publishes_without_Done_because_it_selects_on_every_completion()
    {
        var root = new ActivityNode("if", typeof(IfActivity).FullName!, [Literal(nameof(IfActivity.Condition), true)], [],
            new(IfActivity.StructureKind, IfActivity.StructureSchemaVersion, JsonSerializer.SerializeToElement(new Elsa.Activities.If.Models.IfAuthoredStructure(), Web)));

        var executable = await Compile(root, typeof(IfActivity));

        var outcomes = executable.RootActivity.ActivityContract!.Outcomes;
        Assert.DoesNotContain(ActivityOutcomes.Done, outcomes);
        Assert.Contains(ActivityOutcomes.True, outcomes);
        Assert.Contains(ActivityOutcomes.False, outcomes);
        Assert.False(executable.RootActivity.ActivityContract.AllowsMultipleOutcomes);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("MatchFirst", false)]
    [InlineData("MatchAny", true)]
    public async Task Switch_publishes_without_Done_and_pins_the_multi_outcome_flag_by_mode(string? mode, bool allowsMultiple)
    {
        var executable = await Compile(Switch("switch", mode), typeof(SwitchActivity));

        var contract = executable.RootActivity.ActivityContract!;
        Assert.DoesNotContain(ActivityOutcomes.Done, contract.Outcomes);
        Assert.Contains("a", contract.Outcomes);
        Assert.Contains("b", contract.Outcomes);
        Assert.Contains(ActivityOutcomes.Default, contract.Outcomes);
        Assert.Equal(allowsMultiple, contract.AllowsMultipleOutcomes);
    }

    [Fact]
    public async Task SendHttpRequest_with_expected_codes_publishes_without_Done()
    {
        var node = new ActivityNode("http", typeof(SendHttpRequest).FullName!,
        [
            Literal(nameof(SendHttpRequest.Url), "https://example.invalid/"),
            Literal(nameof(SendHttpRequest.ExpectedStatusCodes), new[] { 200, 404 })
        ], []);

        var executable = await CompileAsync(node, [typeof(SendHttpRequest)], includeDeclaredInputDefaults: true);

        var outcomes = executable.RootActivity.ActivityContract!.Outcomes;
        Assert.DoesNotContain(ActivityOutcomes.Done, outcomes);
        Assert.Contains("200", outcomes);
        Assert.Contains("404", outcomes);
        Assert.Contains(HttpActivityOutcomes.UnmatchedStatusCode, outcomes);
        Assert.Contains(HttpActivityOutcomes.Failed, outcomes);
        Assert.Contains(HttpActivityOutcomes.Timeout, outcomes);
        Assert.False(executable.RootActivity.ActivityContract.AllowsMultipleOutcomes);
    }

    [Fact]
    public async Task RunJavaScript_pins_Done_and_its_possible_outcomes_without_Unmatched_and_allows_several()
    {
        var node = new ActivityNode("script", typeof(RunJavaScript).FullName!,
        [
            Literal("script", "return complete(1, ['A', 'B']);"),
            Literal(RunJavaScript.PossibleOutcomesInputKey, new[] { "A", "B" })
        ], []);

        var executable = await CompileAsync(node, [typeof(RunJavaScript)], includeDeclaredInputDefaults: true);

        var contract = executable.RootActivity.ActivityContract!;
        Assert.Equal(["A", "B", ActivityOutcomes.Done], contract.Outcomes.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("Unmatched", contract.Outcomes);
        Assert.True(contract.AllowsMultipleOutcomes);
    }

    [Fact]
    public async Task RunJavaScript_without_possible_outcomes_pins_Done_only_and_still_allows_several()
    {
        var node = new ActivityNode("script", typeof(RunJavaScript).FullName!, [Literal("script", "return 1;")], []);

        var executable = await CompileAsync(node, [typeof(RunJavaScript)], includeDeclaredInputDefaults: true);

        var contract = executable.RootActivity.ActivityContract!;
        Assert.Equal([ActivityOutcomes.Done], contract.Outcomes);
        Assert.True(contract.AllowsMultipleOutcomes);
    }

    private static Task<WorkflowExecutable> Compile(ActivityNode root, params Type[] activityTypes) =>
        CompileAsync(root, [typeof(FlowchartActivity), .. activityTypes], structures: Structures);

    private static ActivityNode Leaf(string nodeId, Type activityType) => new(nodeId, activityType.FullName!, [], []);

    private static ActivityNode Flowchart(IReadOnlyCollection<ActivityNode> activities, IReadOnlyCollection<FlowchartConnection> connections) =>
        new("flowchart", typeof(FlowchartActivity).FullName!, [], [],
            new(FlowchartActivity.StructureKind, FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartAuthoredStructure(activities, connections, activities.First().NodeId), Web)));

    private static ActivityNode Switch(string nodeId, string? mode) =>
        new(nodeId, typeof(SwitchActivity).FullName!, [Literal(nameof(SwitchActivity.Value), "a")], [],
            new(SwitchActivity.StructureKind, SwitchActivity.StructureSchemaVersion, JsonSerializer.SerializeToElement(
                new SwitchAuthoredStructure([new("a") { CaseId = "case-a" }, new("b") { CaseId = "case-b" }], mode: mode), Web)));

    private static ArgumentState Literal(string key, object value) =>
        new(key, new ArgumentValue(JsonSerializer.SerializeToElement(value), "Literal"), null, null, null, null);

    private static IActivityStructureService StructureService()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        new ActivitiesSequenceFeature().ConfigureServices(services);
        new ActivitiesFlowchartFeature().ConfigureServices(services);
        new Elsa.Activities.ControlFlow.ActivitiesControlFlowFeature().ConfigureServices(services);
        return services.BuildServiceProvider().GetRequiredService<IActivityStructureService>();
    }

    /// <summary>Declares two outcomes without Done and selects one on every completion.</summary>
    [ActivityOutcome("Approved")]
    [ActivityOutcome("Rejected")]
    [SelectsOutcomeOnEveryCompletion]
    public sealed class AlwaysDecides : Activity<string>
    {
        protected override ValueTask<ActivityTransition<string>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete("decided", "Approved"));
    }

    /// <summary>Declares two outcomes without Done and does not guarantee a selection.</summary>
    [ActivityOutcome("Approved")]
    [ActivityOutcome("Rejected")]
    public sealed class MayNotDecide : Activity<string>
    {
        protected override ValueTask<ActivityTransition<string>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete("undecided"));
    }

    /// <summary>An ordinary step: no declared outcomes, so its set is Done.</summary>
    public sealed class PlainStep : Activity<string>
    {
        protected override ValueTask<ActivityTransition<string>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete("step"));
    }
}
