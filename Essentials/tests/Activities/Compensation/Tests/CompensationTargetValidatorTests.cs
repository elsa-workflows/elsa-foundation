using System.Text.Json;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Internal;
using Elsa.Activities.Compensation.Models;
using Elsa.Activities.Compensation.Tests.Support;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Models;
using Xunit;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>
/// Spec 258 FR-315 (quickstart C4): a Confirm or Compensate needs an enclosing scope, names only an enclosing scope,
/// and never sits in a compensation handler. Expression targets are left to the runtime.
/// </summary>
public sealed class CompensationTargetValidatorTests
{
    private const string ConfirmVersion = "activity-version-confirm";
    private const string CompensateVersion = "activity-version-compensate";
    private const string ContainerKind = "test.container";
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private readonly CompensationTargetValidator _validator = new(
        new DefaultActivityStructureService([new CompensationScopeStructureHandler(), new ContainerStructureHandler()]),
        new TypeKeyActivityCatalog()
            .Add(ConfirmVersion, typeof(Confirm).FullName!)
            .Add(CompensateVersion, typeof(Compensate).FullName!)
            .Add("activity-version-leaf", "Test.Leaf"));

    [Fact]
    public async Task A_Confirm_or_Compensate_without_an_enclosing_scope_is_reported()
    {
        var errors = await ValidateAsync(Container("root", Confirm("confirm"), Compensate("compensate")));

        Assert.Equal(
            [("compensate", CompensationTargetValidator.NoEnclosingScopeType), ("confirm", CompensationTargetValidator.NoEnclosingScopeType)],
            errors.Select(error => (error.Path, error.Type)).Order());
    }

    [Fact]
    public async Task A_Confirm_in_a_step_under_a_container_is_valid()
    {
        Assert.Empty(await ValidateAsync(Scope("scope", ("Reserve", Leaf("reserve"), Leaf("release")), ("Check", Container("flow", Confirm("confirm")), null))));
    }

    [Fact]
    public async Task A_named_target_must_be_an_enclosing_scope()
    {
        var root = Container("root",
            Scope("outer", ("Order", Scope("inner", ("Give up", Compensate("compensate", scope: "outer"), null)), null)),
            Scope("sibling", ("Check", Confirm("confirm", scope: "outer"), null)));

        var error = Assert.Single(await ValidateAsync(root));

        Assert.Equal(("confirm", CompensationTargetValidator.TargetNotEnclosingType), (error.Path, error.Type));
    }

    [Fact]
    public async Task A_Confirm_or_Compensate_in_a_compensation_handler_is_reported()
    {
        var root = Scope("scope", ("Reserve", Leaf("reserve"), Container("undo", Leaf("release"), Compensate("compensate"))));

        var error = Assert.Single(await ValidateAsync(root));

        Assert.Equal(("compensate", CompensationTargetValidator.InsideHandlerType), (error.Path, error.Type));
    }

    [Fact]
    public async Task A_named_target_whose_handler_holds_the_node_is_reported()
    {
        var root = Scope("outer", ("Reserve", Leaf("reserve"),
            Scope("inner", ("Check", Confirm("confirm", scope: "outer"), null))));

        var error = Assert.Single(await ValidateAsync(root));

        Assert.Equal(("confirm", CompensationTargetValidator.InsideHandlerType), (error.Path, error.Type));
    }

    [Fact]
    public async Task A_Compensate_whose_cascade_passes_through_a_handler_is_reported()
    {
        // outer → step → middle → handler → inner → step → Compensate(outer): the cascade faults middle's handler.
        var root = Scope("outer", ("Order", Scope("middle", ("Reserve", Leaf("reserve"),
            Scope("inner", ("Give up", Compensate("compensate", scope: "outer"), null)))), null));

        var error = Assert.Single(await ValidateAsync(root));

        Assert.Equal(("compensate", CompensationTargetValidator.InsideHandlerType), (error.Path, error.Type));
        Assert.Contains("'middle'", error.Message);
    }

    [Fact]
    public async Task An_expression_target_is_left_to_the_runtime()
    {
        var node = Compensate("compensate") with
        {
            Inputs = [new ArgumentState("scope", new ArgumentValue("return 'elsewhere';", "JavaScript"), null, null, null, null)]
        };

        Assert.Empty(await ValidateAsync(Scope("scope", ("Check", node, null))));
    }

    private async Task<IReadOnlyList<ValidationError>> ValidateAsync(ActivityNode root) =>
        [.. await _validator.Validate(new StubDraft(new WorkflowDefinitionState([], root, [], [], null)), CancellationToken.None)];

    private static ActivityNode Leaf(string nodeId) => new(nodeId, "activity-version-leaf", [], []);

    private static ActivityNode Confirm(string nodeId, string? scope = null) => new(nodeId, ConfirmVersion, Target(scope), []);

    private static ActivityNode Compensate(string nodeId, string? scope = null) => new(nodeId, CompensateVersion, Target(scope), []);

    private static ArgumentState[] Target(string? scope) =>
        scope is null ? [] : [new ArgumentState("scope", new ArgumentValue(scope, "Literal"), null, null, null, null)];

    private static ActivityNode Scope(string nodeId, params (string Name, ActivityNode? Activity, ActivityNode? Compensation)[] steps)
    {
        var structure = new CompensationScopeAuthoredStructure(
            steps.Select(step => new CompensationScopeAuthoredStep(step.Name, step.Activity, step.Compensation)).ToArray());
        return new ActivityNode(nodeId, $"activity-version-{nodeId}", [], [],
            new ActivityNodeStructure(CompensationScope.StructureKind, CompensationScope.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, WebOptions)));
    }

    private static ActivityNode Container(string nodeId, params ActivityNode[] children) =>
        new(nodeId, "activity-version-container", [], [],
            new ActivityNodeStructure(ContainerKind, "1", JsonSerializer.SerializeToElement(new { activities = children }, WebOptions)));

    private sealed class StubDraft(WorkflowDefinitionState state) : IWorkflowDefinitionDraft
    {
        public string Id => "draft-1";
        public string WorkflowDefinitionId => "wf-1";
        public WorkflowDefinitionState State { get; } = state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UnixEpoch;
    }

    // A plain container standing in for a Flowchart or Sequence: one slot with every child.
    private sealed class ContainerStructureHandler : IActivityStructureHandler
    {
        public string Kind => ContainerKind;
        public string SchemaVersion => "1";

        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) =>
            [new("Container.Activities", activity.Structure!.Payload.GetProperty("activities").Deserialize<ActivityNode[]>(WebOptions) ?? [])];

        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => throw new NotSupportedException();
        public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity) => throw new NotSupportedException();
    }
}
