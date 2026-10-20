using System.Text.Json;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Internal;
using Elsa.Activities.Compensation.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>
/// Design-side coverage: the feature's registrations, the structure handler's project/replace/compile/remap round trip
/// with two slots per step, and the Draft validator's messages and paths.
/// </summary>
public sealed class CompensationScopeDesignTests : IDisposable
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureHandler _handler;
    private readonly IActivityStructureService _structureService;

    public CompensationScopeDesignTests()
    {
        var services = new ServiceCollection();
        new ActivitiesCompensationFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _handler = Assert.Single(_provider.GetServices<IActivityStructureHandler>());
        _structureService = new DefaultActivityStructureService(_provider.GetServices<IActivityStructureHandler>());
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void The_feature_registers_the_structure_handler_and_the_Draft_validators()
    {
        Assert.Equal(CompensationScope.StructureKind, _handler.Kind);
        Assert.Equal(CompensationScope.StructureSchemaVersion, _handler.SchemaVersion);
        Assert.Equal(typeof(CompensationScopeAuthoredStructure), _handler.AuthoredPayloadType);

        var services = new ServiceCollection();
        new ActivitiesCompensationFeature().ConfigureServices(services);
        var validators = services.Where(descriptor => descriptor.ServiceType == typeof(IDraftValidator)).ToArray();
        Assert.Equal([typeof(CompensationScopeValidator), typeof(CompensationTargetValidator), typeof(CompensationHandlerVariableValidator)], validators.Select(validator => validator.ImplementationType));
        Assert.All(validators, validator => Assert.Equal(ServiceLifetime.Scoped, validator.Lifetime));
    }

    [Fact]
    public void Each_step_projects_a_forward_slot_and_a_compensation_slot()
    {
        var node = ScopeNode("scope", ("Reserve", Leaf("reserve"), Leaf("release")), ("Confirm", Leaf("confirm"), null));

        var slots = _handler.ProjectChildren(node);

        Assert.Equal(
            ["CompensationScope.Step[Reserve]", "CompensationScope.Compensation[Reserve]", "CompensationScope.Step[Confirm]", "CompensationScope.Compensation[Confirm]"],
            slots.Select(slot => slot.Name));
        Assert.Equal("release", Assert.Single(slots.Single(slot => slot.Name == "CompensationScope.Compensation[Reserve]").Activities).NodeId);
        Assert.Empty(slots.Single(slot => slot.Name == "CompensationScope.Compensation[Confirm]").Activities);
    }

    [Fact]
    public void A_handler_is_offered_its_own_step_s_result_first_and_a_step_the_steps_before_it()
    {
        var node = ScopeNode("scope",
            ("Inventory", Leaf("reserve"), Leaf("release")),
            ("Delivery", Leaf("deliver"), Leaf("cancel")),
            ("Confirm", Leaf("confirm"), null));

        Assert.Equal(["deliver", "reserve"], _structureService.ChildrenCompletedBefore(node, "cancel"));
        Assert.Equal(["reserve"], _structureService.ChildrenCompletedBefore(node, "release"));
        Assert.Equal(["deliver", "reserve"], _structureService.ChildrenCompletedBefore(node, "confirm"));
        Assert.Empty(_structureService.ChildrenCompletedBefore(node, "reserve")!);
        Assert.Null(_structureService.ChildrenCompletedBefore(node, "elsewhere"));
    }

    [Fact]
    public void Replacing_children_round_trips_through_compile_and_remap()
    {
        var node = ScopeNode("scope", ("Reserve", Leaf("reserve"), null), ("Confirm", Leaf("confirm"), null));
        var replaced = _handler.ReplaceChildren(node,
        [
            new ActivityChildProjection(CompensationScope.StepSlotName("Reserve"), [Leaf("reserve-2")]),
            new ActivityChildProjection(CompensationScope.CompensationSlotName("Reserve"), [Leaf("release-2")]),
            new ActivityChildProjection(CompensationScope.StepSlotName("Confirm"), [Leaf("confirm")])
        ]);

        var compiled = _handler.CompileExecutableStructure(replaced);
        var remapped = _handler.RemapExecutableStructure(compiled, new Dictionary<string, string>
        {
            ["reserve-2"] = "x-reserve",
            ["release-2"] = "x-release",
            ["confirm"] = "x-confirm"
        });

        var executable = remapped.Payload.Deserialize<CompensationScopeExecutableStructure>(WebOptions)!;
        Assert.Collection(
            executable.Steps,
            step => Assert.Equal(("Reserve", "x-reserve", "x-release"), (step.Name, step.Activity, step.Compensation)),
            step => Assert.Equal(("Confirm", "x-confirm", (string?)null), (step.Name, step.Activity, step.Compensation)));
    }

    [Fact]
    public void Steps_that_share_a_name_keep_their_own_activities_through_a_structure_round_trip()
    {
        // A Draft with a duplicate name is saved (the validator only blocks publication), so every structure rewrite
        // in between must keep each step's own activities.
        var node = ScopeNode("scope", ("Same", Leaf("first"), Leaf("first-undo")), ("Same", Leaf("second"), null));

        var replaced = _handler.ReplaceChildren(node, _handler.ProjectChildren(node));

        Assert.Collection(
            CompensationScopeAuthoredStructureReader.Read(replaced).Steps,
            step => Assert.Equal(("first", "first-undo"), (step.Activity?.NodeId, step.Compensation?.NodeId)),
            step => Assert.Equal(("second", (string?)null), (step.Activity?.NodeId, step.Compensation?.NodeId)));
    }

    [Theory]
    [InlineData(" ", "has a step without a name")]
    [InlineData("Same", "more than once")]
    public void Compiling_refuses_invalid_step_names(string secondName, string expected)
    {
        var node = ScopeNode("scope", ("Same", Leaf("a"), null), (secondName, Leaf("b"), null));

        var exception = Assert.Throws<ArgumentException>(() => _handler.CompileExecutableStructure(node));
        Assert.Contains(expected, exception.Message);
    }

    [Fact]
    public void Compiling_refuses_a_step_without_an_activity()
    {
        var node = ScopeNode("scope", ("Reserve", null, Leaf("release")));

        var exception = Assert.Throws<ArgumentException>(() => _handler.CompileExecutableStructure(node));
        Assert.Contains("has no activity", exception.Message);
    }

    [Fact]
    public async Task The_validator_reports_each_invalid_step_with_its_row_path()
    {
        var node = ScopeNode("scope", ("Reserve", Leaf("a"), null), ("Reserve", Leaf("b"), null), ("", Leaf("c"), null), ("Confirm", null, null));

        var errors = await ValidateAsync(node);

        Assert.Contains(errors, error => error is { Type: CompensationScopeValidator.DuplicateNameType, Path: "scope/steps/0" } && error.Message.Contains("\"Reserve\""));
        Assert.Contains(errors, error => error is { Type: CompensationScopeValidator.BlankNameType, Path: "scope/steps/2" });
        Assert.Contains(errors, error => error is { Type: CompensationScopeValidator.MissingActivityType, Path: "scope/steps/3" } && error.Message.Contains("\"Confirm\""));
        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public async Task The_validator_walks_into_nested_scopes()
    {
        var inner = ScopeNode("inner", ("Dup", Leaf("a"), null), ("Dup", Leaf("b"), null));
        var outer = ScopeNode("outer", ("Inner", inner, null));

        var error = Assert.Single(await ValidateAsync(outer));

        Assert.Equal("inner/steps/0", error.Path);
    }

    [Fact]
    public async Task A_valid_scope_has_no_errors()
    {
        Assert.Empty(await ValidateAsync(ScopeNode("scope", ("Reserve", Leaf("a"), Leaf("b")), ("Confirm", Leaf("c"), null))));
    }

    private async Task<IReadOnlyList<ValidationError>> ValidateAsync(ActivityNode rootActivity)
    {
        var validator = new CompensationScopeValidator(_structureService);
        var state = new WorkflowDefinitionState([], rootActivity, [], [], null);
        return [.. await validator.Validate(new StubDraft(state), CancellationToken.None)];
    }

    private static ActivityNode Leaf(string nodeId) => new(nodeId, $"activity-version-{nodeId}", [], []);

    private static ActivityNode ScopeNode(string nodeId, params (string Name, ActivityNode? Activity, ActivityNode? Compensation)[] steps)
    {
        var structure = new CompensationScopeAuthoredStructure(
            steps.Select(step => new CompensationScopeAuthoredStep(step.Name, step.Activity, step.Compensation)).ToArray());
        return new ActivityNode(nodeId, $"activity-version-{nodeId}", [], [],
            new ActivityNodeStructure(CompensationScope.StructureKind, CompensationScope.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, WebOptions)));
    }

    private sealed class StubDraft(WorkflowDefinitionState state) : IWorkflowDefinitionDraft
    {
        public string Id => "draft-1";
        public string WorkflowDefinitionId => "wf-1";
        public WorkflowDefinitionState State { get; } = state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UtcNow;
    }
}
