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
    public void MatchAny_with_a_case_activity_is_refused_at_compile()
    {
        using var provider = StructureProvider();
        var handler = provider.GetServices<Elsa.Workflows.Design.Core.Contracts.IActivityStructureHandler>()
            .Single(item => item.Kind == SwitchActivity.StructureKind && item.SchemaVersion == SwitchActivity.StructureSchemaVersion);
        var branch = new ActivityNode("branch", "av-branch", [], []);
        var node = AuthoredSwitch(SwitchModes.MatchAny, new SwitchAuthoredCase("Approved", branch) { CaseId = "a" }, new SwitchAuthoredCase("Audit") { CaseId = "b" });

        var error = Assert.Throws<ArgumentException>(() => handler.CompileExecutableStructure(node));

        Assert.Contains("MatchAny", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Approved'", error.Message, StringComparison.Ordinal);
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
    public void An_unknown_mode_is_refused_at_compile()
    {
        using var provider = StructureProvider();
        var handler = provider.GetServices<Elsa.Workflows.Design.Core.Contracts.IActivityStructureHandler>()
            .Single(item => item.Kind == SwitchActivity.StructureKind && item.SchemaVersion == SwitchActivity.StructureSchemaVersion);

        var error = Assert.Throws<ArgumentException>(() => handler.CompileExecutableStructure(AuthoredSwitch("MatchSome", new SwitchAuthoredCase("Approved") { CaseId = "a" })));

        Assert.Contains("'MatchSome'", error.Message, StringComparison.Ordinal);
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
        .Build("switch-invocation", "branch-invocation");

    // Three empty cases with pinned Boolean conditions and a Default branch, as the publish compiler pins a Switch.
    private static WorkflowExecutable Executable(string? mode, bool approved, bool audit, bool notify)
    {
        var labels = new[] { ("Approved", "a", approved), ("Audit", "b", audit), ("Notify", "c", notify) };
        var payload = JsonSerializer.SerializeToElement(new
        {
            cases = labels.Select(item => new { match = item.Item1, activity = (string?)null, conditionInputKey = SwitchActivity.ConditionInputKey(item.Item2) }).ToArray(),
            @default = "fallback",
            mode
        });
        var bindings = labels.Select(item => Binding(item.Item2, item.Item3)).ToDictionary(binding => binding.InputKey);
        var prototype = ClrActivityContractTestBuilder.BuildContract(typeof(SwitchActivity));
        var contract = new ActivityContract(prototype.ActivityTypeKey, "4.1.0", prototype.DescriptorKind, prototype.DescriptorPayload,
            bindings.Values.Select(binding => new ActivityInputContract(binding.InputKey, binding.InputKey,
                new ValueTypeDescriptor("Boolean"), false, false, false, null, ActivityValuePolicy.Default) { IsStructureInput = true }),
            prototype.Result,
            ["Approved", "Audit", "Notify", ActivityOutcomes.Default, ActivityOutcomes.Break],
            prototype.Activation, prototype.SideEffectProfile, supportsBackgroundExecution: false,
            allowsMultipleOutcomes: mode == SwitchModes.MatchAny);
        var node = new ExecutableNode("switch", "switch", typeof(SwitchActivity).FullName!, "4.1.0",
            descriptorType: prototype.DescriptorKind, descriptorPayload: prototype.DescriptorPayload,
            inputBindings: bindings, metadata: new Dictionary<string, string>(),
            childSlots: [new(SwitchActivity.DefaultSlotName, [WorkflowExecutionHarness.NewProbeNode("fallback")])],
            structure: new ExecutableActivityStructure(SwitchActivity.StructureKind, SwitchActivity.StructureSchemaVersion, payload),
            activityContract: contract);
        return WorkflowExecutionHarness.NewExecutable(node);
    }

    private static RuntimeInputBinding Binding(string id, bool value) => new(
        SwitchActivity.ConditionInputKey(id), new ValueTypeDescriptor("Boolean"), ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.Literal,
        literal: ValueEnvelope.Inline(new ValueTypeDescriptor("Boolean"), JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
}
