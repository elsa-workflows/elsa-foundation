using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Switch.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Activities.ControlFlow.Tests.NamedSelectionConformance;
using SwitchActivity = Elsa.Activities.Switch.Activities.Switch;

namespace Elsa.Activities.Switch.Tests;

/// <summary>
/// Spec 1208 FR-031: a Switch in <c>MatchAny</c> mode selects every matching case, in case order, or <c>Default</c>
/// when none match; its cases cannot hold activities. <c>MatchFirst</c> (the default) keeps selecting the first match.
/// </summary>
public sealed class SwitchMatchAnyTests
{
    [Theory]
    [InlineData(true, true, true, new[] { "Approved", "Audit", "Notify" })]
    [InlineData(true, false, true, new[] { "Approved", "Notify" })]
    [InlineData(false, true, false, new[] { "Audit" })]
    public async Task MatchAny_selects_every_matching_case_in_case_order(bool approved, bool audit, bool notify, string[] expected)
    {
        await using var harness = NewHarness();

        var run = await harness.RunAsync(Executable(SwitchModes.MatchAny, approved, audit, notify));

        run.AssertOutcomes("switch", expected);
        run.AssertDidNotRun("fallback");
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task MatchAny_with_no_match_runs_Default()
    {
        await using var harness = NewHarness();

        var run = await harness.RunAsync(Executable(SwitchModes.MatchAny, false, false, false));

        run.AssertOutcomes("switch", ActivityOutcomes.Default);
        run.AssertRan("fallback");
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task MatchFirst_still_selects_only_the_first_match()
    {
        await using var harness = NewHarness();

        var run = await harness.RunAsync(Executable(mode: null, true, true, true));

        run.AssertOutcomes("switch", "Approved");
        run.AssertDidNotRun("fallback");
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public void MatchAny_with_case_activities_compiles_and_remaps()
    {
        using var provider = StructureProvider();
        var handler = provider.GetServices<Elsa.Workflows.Design.Core.Contracts.IActivityStructureHandler>()
            .Single(item => item.Kind == SwitchActivity.StructureKind && item.SchemaVersion == SwitchActivity.StructureSchemaVersion);
        var branch = new ActivityNode("branch", "av-branch", [], []);
        var node = AuthoredSwitch(SwitchModes.MatchAny, new SwitchAuthoredCase("Approved", branch) { CaseId = "a" }, new SwitchAuthoredCase("Audit") { CaseId = "b" });

        var compiled = handler.CompileExecutableStructure(node);
        var remapped = handler.RemapExecutableStructure(compiled, new Dictionary<string, string> { ["branch"] = "placed-branch" });
        var structure = remapped.Payload.Deserialize<SwitchExecutableStructure>()!;
        Assert.True(structure.MatchesAny);
        Assert.Equal("placed-branch", structure.Cases.First().Activity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_matching_populated_case_runs_once_and_empty_matches_still_select(bool emptyFirst)
    {
        await using var harness = NewHarness();
        var bodies = new Dictionary<string, ExecutableNode>
        {
            ["b"] = WorkflowExecutionHarness.NewProbeNode("false-body"),
            ["c"] = WorkflowExecutionHarness.NewProbeNode("notify-body")
        };
        if (!emptyFirst) bodies["a"] = WorkflowExecutionHarness.NewProbeNode("approved-body");
        var run = await harness.RunAsync(Executable(SwitchModes.MatchAny, true, false, true, bodies));
        run.AssertOutcomes("switch", "Approved", "Notify");
        run.AssertCompleted("notify-body");
        if (!emptyFirst) run.AssertCompleted("approved-body");
        run.AssertDidNotRun("false-body", "fallback");
        Assert.Single(run.States("switch"));
        if (!emptyFirst) Assert.NotEqual(run.State("approved-body").BranchId, run.State("notify-body").BranchId);
        run.AssertWorkflowCompleted();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task No_matches_with_populated_cases_selects_only_optional_Default(bool includeDefault)
    {
        await using var harness = NewHarness();
        var run = await harness.RunAsync(Executable(SwitchModes.MatchAny, false, false, false,
            new Dictionary<string, ExecutableNode> { ["a"] = WorkflowExecutionHarness.NewProbeNode("not-selected") }, includeDefault));
        run.AssertOutcomes("switch", ActivityOutcomes.Default);
        run.AssertDidNotRun("not-selected");
        Assert.Equal(includeDefault, run.DidRun("fallback"));
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public void MatchAny_bodies_reject_reserved_Done_before_publication()
    {
        using var provider = StructureProvider();
        var handler = provider.GetServices<Elsa.Workflows.Design.Core.Contracts.IActivityStructureHandler>()
            .Single(item => item.Kind == SwitchActivity.StructureKind && item.SchemaVersion == SwitchActivity.StructureSchemaVersion);
        var node = AuthoredSwitch(SwitchModes.MatchAny, new SwitchAuthoredCase("Done", new ActivityNode("branch", "av", [], [])) { CaseId = "a" });
        Assert.Contains("reserved case label 'Done'", Assert.Throws<ArgumentException>(() => handler.CompileExecutableStructure(node)).Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(SwitchModes.MatchFirst)]
    [InlineData(SwitchModes.MatchAny)]
    public void Empty_cases_compile_in_every_mode_and_MatchAny_is_kept_in_the_executable_structure(string? mode)
    {
        using var provider = StructureProvider();
        var handler = provider.GetServices<Elsa.Workflows.Design.Core.Contracts.IActivityStructureHandler>()
            .Single(item => item.Kind == SwitchActivity.StructureKind && item.SchemaVersion == SwitchActivity.StructureSchemaVersion);

        var compiled = handler.CompileExecutableStructure(AuthoredSwitch(mode, new SwitchAuthoredCase("Approved") { CaseId = "a" }));

        var structure = compiled.Payload.Deserialize<SwitchExecutableStructure>()!;
        Assert.Equal(mode == SwitchModes.MatchAny, structure.MatchesAny);
    }

    [Fact]
    public void Remapping_the_executable_structure_keeps_MatchAny()
    {
        using var provider = StructureProvider();
        var handler = provider.GetServices<Elsa.Workflows.Design.Core.Contracts.IActivityStructureHandler>()
            .Single(item => item.Kind == SwitchActivity.StructureKind && item.SchemaVersion == SwitchActivity.StructureSchemaVersion);
        var compiled = handler.CompileExecutableStructure(AuthoredSwitch(SwitchModes.MatchAny, new SwitchAuthoredCase("Approved") { CaseId = "a" }));

        // A reusable activity body is remapped when it is placed; the mode must survive it.
        var remapped = handler.RemapExecutableStructure(compiled, new Dictionary<string, string> { ["unused"] = "placed-unused" });

        Assert.True(remapped.Payload.Deserialize<SwitchExecutableStructure>()!.MatchesAny);
    }

    [Fact]
    public void An_unknown_mode_is_refused_at_compile()
    {
        using var provider = StructureProvider();
        var handler = provider.GetServices<Elsa.Workflows.Design.Core.Contracts.IActivityStructureHandler>()
            .Single(item => item.Kind == SwitchActivity.StructureKind && item.SchemaVersion == SwitchActivity.StructureSchemaVersion);

        var error = Assert.Throws<ArgumentException>(() => handler.CompileExecutableStructure(AuthoredSwitch("MatchSome", new SwitchAuthoredCase("Approved") { CaseId = "a" })));

        Assert.Contains("'MatchSome'", error.Message, StringComparison.Ordinal);
    }

    // Spec 1208 FR-002b: in either mode every completion selects by name, the fallback included.
    [Theory]
    [InlineData(SwitchModes.MatchAny, true, false, true, new[] { "Approved", "Notify" })]
    [InlineData(SwitchModes.MatchAny, false, false, false, new[] { ActivityOutcomes.Default })]
    [InlineData(SwitchModes.MatchFirst, true, true, true, new[] { "Approved" })]
    [InlineData(SwitchModes.MatchFirst, false, false, false, new[] { ActivityOutcomes.Default })]
    public async Task Every_completion_selects_a_named_outcome(string mode, bool approved, bool audit, bool notify, string[] expected)
    {
        await using var harness = NewHarness();

        var run = await harness.RunAsync(Executable(mode, approved, audit, notify));

        AssertNamedSelection(run, "switch", expected);
    }

    private static ServiceProvider StructureProvider()
    {
        var services = new ServiceCollection();
        new ActivitiesControlFlowFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private static ActivityNode AuthoredSwitch(string? mode, params SwitchAuthoredCase[] cases) =>
        new("switch", "av-switch", [], [],
            new ActivityNodeStructure(SwitchActivity.StructureKind, SwitchActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new SwitchAuthoredStructure(cases, mode: mode), new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    private static WorkflowExecutionHarness NewHarness() => WorkflowExecutionHarness.Create()
        .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
        .WithProbeLeaf()
        .Build("switch-invocation", "branch-invocation", "body-2", "body-3", "body-4");

    // Three empty cases with pinned Boolean conditions and a Default branch, as the publish compiler pins a Switch.
    internal static WorkflowExecutable Executable(string? mode, bool approved, bool audit, bool notify,
        IReadOnlyDictionary<string, ExecutableNode>? bodies = null, bool includeDefault = true)
    {
        bodies ??= new Dictionary<string, ExecutableNode>();
        var labels = new[] { ("Approved", "a", approved), ("Audit", "b", audit), ("Notify", "c", notify) };
        var payload = JsonSerializer.SerializeToElement(new
        {
            cases = labels.Select(item => new { match = item.Item1, activity = bodies.GetValueOrDefault(item.Item2)?.ExecutableNodeId, conditionInputKey = SwitchActivity.ConditionInputKey(item.Item2) }).ToArray(),
            @default = includeDefault ? "fallback" : null,
            mode
        });
        var bindings = labels.Select(item => Binding(item.Item2, item.Item3)).ToDictionary(binding => binding.InputKey);
        var prototype = ClrActivityContractTestBuilder.BuildContract(typeof(SwitchActivity));
        var contract = new ActivityContract(prototype.ActivityTypeKey, "4.1.0", prototype.DescriptorKind, prototype.DescriptorPayload,
            bindings.Values.Select(binding => new ActivityInputContract(binding.InputKey, binding.InputKey,
                new ValueTypeDescriptor("Boolean"), false, false, false, null, ActivityValuePolicy.Default) { IsStructureInput = true }),
            prototype.Result,
            ["Approved", "Audit", "Notify", ActivityOutcomes.Default, ActivityOutcomes.Break],
            prototype.Activation, prototype.SideEffectProfile, supportsBackgroundExecution: false);
        var slots = labels.Where(item => bodies.ContainsKey(item.Item2))
            .Select(item => new ExecutableChildSlot(SwitchActivity.CaseSlotName(item.Item1), [bodies[item.Item2]])).ToList();
        if (includeDefault) slots.Add(new(SwitchActivity.DefaultSlotName, [WorkflowExecutionHarness.NewProbeNode("fallback")]));
        var node = new ExecutableNode("switch", "switch", typeof(SwitchActivity).FullName!, "4.1.0",
            descriptorType: prototype.DescriptorKind, descriptorPayload: prototype.DescriptorPayload,
            inputBindings: bindings, metadata: new Dictionary<string, string>(),
            childSlots: slots,
            structure: new ExecutableActivityStructure(SwitchActivity.StructureKind, SwitchActivity.StructureSchemaVersion, payload),
            activityContract: contract);
        return WorkflowExecutionHarness.NewExecutable(node);
    }

    private static RuntimeInputBinding Binding(string id, bool value) => new(
        SwitchActivity.ConditionInputKey(id), new ValueTypeDescriptor("Boolean"), ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.Literal,
        literal: ValueEnvelope.Inline(new ValueTypeDescriptor("Boolean"), JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
}
