using System.Text.Json;
using Elsa.Activities.Graph.Design;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Graph.Tests;

/// <summary>Spec 237 FR-016: a workflow definition, which is never a graph body, cannot contain a Complete node.</summary>
public sealed class CompleteOutsideGraphDraftValidatorTests
{
    private const string CompleteVersionId = "activity-ver-complete";
    private const string ContainerKind = "test.container";

    private readonly CompleteOutsideGraphDraftValidator _validator = new(
        new ContainerStructureService(),
        new TypeKeyActivityCatalog()
            .Add(CompleteVersionId, GraphActivityProvider.CompleteActivityTypeKey)
            .Add("activity-ver-step", "Elsa.Activities.Primitives.Activities.WriteLine"));

    [Fact]
    public async Task A_root_Complete_node_is_reported()
    {
        var error = Assert.Single(await Validate(Leaf("exit", CompleteVersionId)));

        Assert.Equal("exit", error.Path);
        Assert.Equal(CompleteOutsideGraphDraftValidator.ErrorType, error.Type);
    }

    [Fact]
    public async Task A_nested_Complete_node_is_reported()
    {
        var root = Container("root", Leaf("step", "activity-ver-step"), Container("inner", Leaf("exit", CompleteVersionId)));

        var error = Assert.Single(await Validate(root));

        Assert.Equal("exit", error.Path);
    }

    [Fact]
    public async Task A_draft_without_Complete_nodes_is_valid()
    {
        Assert.Empty(await Validate(Container("root", Leaf("step", "activity-ver-step"))));
    }

    [Fact]
    public async Task A_version_the_catalog_cannot_resolve_is_ignored()
    {
        Assert.Empty(await Validate(Leaf("unknown", "activity-ver-unknown")));
    }

    [Fact]
    public void The_design_feature_registers_the_validator()
    {
        var services = new ServiceCollection();
        new GraphActivitiesDesignFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IDraftValidator) &&
            descriptor.ImplementationType == typeof(CompleteOutsideGraphDraftValidator) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    private async Task<IReadOnlyList<ValidationError>> Validate(ActivityNode root) =>
        [.. await _validator.Validate(new StubDraft(new WorkflowDefinitionState([], root, [], [], null)), CancellationToken.None)];

    private static ActivityNode Leaf(string nodeId, string versionId) => new(nodeId, versionId, [], []);

    private static ActivityNode Container(string nodeId, params ActivityNode[] children) =>
        new(nodeId, "activity-ver-container", [], [],
            new ActivityNodeStructure(ContainerKind, "1", JsonSerializer.SerializeToElement(new { activities = children }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    private sealed class StubDraft(WorkflowDefinitionState state) : IWorkflowDefinitionDraft
    {
        public string Id => "draft-1";
        public string WorkflowDefinitionId => "wf-1";
        public WorkflowDefinitionState State { get; } = state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UnixEpoch;
    }

    // Only child projection matters to the validator's tree walk.
    private sealed class ContainerStructureService : IActivityStructureService
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) =>
            activity.Structure is { Kind: ContainerKind } structure
                ? [new("Container.Activities", structure.Payload.GetProperty("activities").Deserialize<ActivityNode[]>(Options) ?? [])]
                : [];

        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => throw new NotSupportedException();
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => throw new NotSupportedException();
        public bool HasHandler(ActivityNodeStructure structure) => throw new NotSupportedException();
        public IReadOnlyCollection<ActivityChildContractMemberUsage> ProjectChildContractMemberUsage(ActivityNode activity) => throw new NotSupportedException();
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => throw new NotSupportedException();
        public bool SupportsScopedVariables(ActivityNode activity) => throw new NotSupportedException();
    }
}
