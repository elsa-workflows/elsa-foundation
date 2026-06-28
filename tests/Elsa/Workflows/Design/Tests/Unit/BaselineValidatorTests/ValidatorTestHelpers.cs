using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Internal;
using Microsoft.Extensions.Options;
using InputDefinition = Elsa.Activities.Design.Core.Models.InputDefinition;
using OutputDefinition = Elsa.Activities.Design.Core.Models.OutputDefinition;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

internal static class ValidatorTestHelpers
{
    public static WorkflowDefinitionState State(
        IEnumerable<ActivityNode>? activities = null,
        IEnumerable<VariableDefinition>? variables = null,
        IEnumerable<InputDefinition>? inputs = null,
        IEnumerable<OutputDefinition>? outputs = null
    ) => new(
        Variables: variables ?? [],
        RootActivity: RootActivity(activities),
        Inputs: inputs ?? [],
        Outputs: outputs ?? [],
        WorkflowActivityOptions: null,
        StrategyOptions: null
    );

    public static WorkflowDefinitionState StateWithRoot(
        ActivityNode rootActivity,
        IEnumerable<VariableDefinition>? variables = null,
        IEnumerable<InputDefinition>? inputs = null,
        IEnumerable<OutputDefinition>? outputs = null
    ) => new(
        Variables: variables ?? [],
        RootActivity: rootActivity,
        Inputs: inputs ?? [],
        Outputs: outputs ?? [],
        WorkflowActivityOptions: null,
        StrategyOptions: null
    );

    public static OnDraftValidating EventFor(WorkflowDefinitionState state) =>
        new(new StubDraft(state));

    public static async Task<IReadOnlyList<ValidationError>> Validate(IDraftValidator validator, WorkflowDefinitionState state) =>
        [.. await validator.Validate(new StubDraft(state), CancellationToken.None)];

    public static IOptions<WorkflowDesignValidatorOptions> Options(int maxRecursionDepth = 100) =>
        Microsoft.Extensions.Options.Options.Create(new WorkflowDesignValidatorOptions { MaxRecursionDepth = maxRecursionDepth });

    public static ActivityTreeWalker Walker() =>
        new(new DefaultActivityStructureService([new TestActivityStructureHandler()]));

    public static ActivityNode Node(
        string nodeId,
        string activityVersionId = "av-1",
        IEnumerable<ArgumentState>? inputs = null,
        IEnumerable<ArgumentState>? outputs = null,
        bool isStart = false,
        IEnumerable<ActivityNode>? childActivities = null
    ) => new(
        NodeId: nodeId,
        ActivityVersionId: activityVersionId,
        Inputs: inputs ?? [],
        Outputs: outputs ?? [],
        Structure: childActivities is null
            ? null
            : TestActivityStructureHandler.CreateStructure(childActivities, childActivities.FirstOrDefault()?.NodeId)
    );

    public static VariableDefinition Variable(string referenceKey, string name) => new(
        ReferenceKey: referenceKey,
        Name: name,
        TypeInformation: Primitives.Models.TypeInformation.String,
        StorageDriverType: null,
        Default: null
    );

    public static ArgumentState VariableInput(string referenceKey, object? variableReference) =>
        new(referenceKey, new ArgumentValue(variableReference, "Variable"), null, null, null, null);

    public static ArgumentState LiteralInput(string referenceKey, object? literalValue) =>
        new(referenceKey, new ArgumentValue(literalValue, "Literal"), null, null, null, null);

    private static ActivityNode? RootActivity(IEnumerable<ActivityNode>? activities)
    {
        var activitySnapshot = (activities ?? []).ToArray();
        if (activitySnapshot.Length == 0)
            return null;

        var startActivityNodeId = activitySnapshot.FirstOrDefault(activity => activity.NodeId == "start")?.NodeId;
        return new ActivityNode(
            NodeId: "$root",
            ActivityVersionId: "$workflow-root",
            Inputs: [],
            Outputs: [],
            Structure: TestActivityStructureHandler.CreateStructure(activitySnapshot, startActivityNodeId));
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
