using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Switch;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;
using SwitchActivity = Elsa.Activities.Switch.Activities.Switch;

namespace Elsa.Activities.Switch.Tests;

/// <summary>
/// In-process execution coverage for the <c>Switch</c> composite running through the real workflow agent.
/// Built on the shared <see cref="WorkflowExecutionHarness"/>: this file only declares the Switch-specific
/// activity graph shape; provider wiring, CLR activation, execution, and assertions come from the harness.
/// Asserts the matching case branch runs (and only it), the default runs on no-match, and the composite
/// emits the matching case / default outcome.
/// </summary>
public sealed class SwitchRuntimeTests
{
    private static readonly (string Match, string Node)[] Cases = [("a", "node-a"), ("b", "node-b")];

    [Fact]
    public async Task MatchingCase_RunsThatBranch_AndEmitsCaseOutcome()
    {
        await using var harness = NewHarness("actexec-switch", "actexec-b");

        var run = await harness.RunAsync(NewExecutable(value: "b"));

        run.AssertOutcomes("node-switch", "b");
        run.AssertRan("node-b");
        run.AssertDidNotRun("node-a", "node-default");
    }

    [Fact]
    public async Task NoMatch_RunsDefaultBranch_AndEmitsDefaultOutcome()
    {
        await using var harness = NewHarness("actexec-switch", "actexec-default");

        var run = await harness.RunAsync(NewExecutable(value: "zzz"));

        run.AssertOutcomes("node-switch", ActivityOutcomes.Default);
        run.AssertRan("node-default");
        run.AssertDidNotRun("node-a", "node-b");
    }

    [Fact]
    public async Task TerminalCaseBranch_CompletesWorkflow()
    {
        await using var harness = NewHarness("actexec-switch", "actexec-a");

        var run = await harness.RunAsync(NewExecutable(value: "a"));

        run.AssertRan("node-a");
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task SelectedCaseBranchIsEmpty_FinalizesWithoutSchedulingChild_AndCompletesWorkflow()
    {
        // Value selects case "a", but its slot is empty: the composite must finalize via
        // The returned structural completion ("a" outcome) schedules no child, and the run must complete.
        await using var harness = NewHarness("actexec-switch");

        var run = await harness.RunAsync(NewExecutable(value: "a", includeCaseBranch: false));

        run.AssertOutcomes("node-switch", "a");
        run.AssertDidNotRun("node-a", "node-b", "node-default");
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task NullValue_RunsDefaultBranch_AndEmitsDefaultOutcome()
    {
        // A null (or unbound) Value never equals any non-nullable case match value, so selection falls
        // through to Default. Locks the behavior the README/<remarks> document.
        await using var harness = NewHarness("actexec-switch", "actexec-default");

        var run = await harness.RunAsync(NewExecutable(value: null));

        run.AssertOutcomes("node-switch", ActivityOutcomes.Default);
        run.AssertRan("node-default");
        run.AssertDidNotRun("node-a", "node-b");
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithProbeLeaf()
            .Build(activityExecutionIds);

    private static WorkflowExecutable NewExecutable(string? value, bool includeCaseBranch = true)
    {
        var childSlots = new List<ExecutableChildSlot>();
        foreach (var (match, node) in Cases)
            if (includeCaseBranch)
                childSlots.Add(new ExecutableChildSlot(SwitchActivity.CaseSlotName(match), [WorkflowExecutionHarness.NewProbeNode(node)]));
        childSlots.Add(new ExecutableChildSlot(SwitchActivity.DefaultSlotName, [WorkflowExecutionHarness.NewProbeNode("node-default")]));

        var root = new ExecutableNode(
            executableNodeId: "node-switch",
            authoredActivityId: "authored-switch",
            activityType: typeof(SwitchActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: typeof(SwitchDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new SwitchDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Value"] = new RuntimeInputBinding(
                    "Value",
                    new ValueTypeDescriptor("String"),
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(
                        new ValueTypeDescriptor("String"),
                        JsonSerializer.SerializeToElement(value),
                        ValueProtectionPolicy.InstanceInline))
            },
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: new ExecutableActivityStructure(
                SwitchActivity.StructureKind,
                SwitchActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new
                {
                    cases = Cases.Select(@case => new { match = @case.Match, activity = includeCaseBranch ? @case.Node : null }).ToArray(),
                    @default = "node-default"
                })));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private sealed record SwitchDescriptor;
}
