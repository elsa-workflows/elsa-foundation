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
    /// <summary>
    /// Synthetic activity-version id assigned to the <c>$root</c> container node that
    /// <see cref="RootActivity"/> wraps the test's activities under. It stands in for the real root
    /// activity's catalog version — a fail-closed lookup fake must resolve it (as an empty version, no
    /// required args) rather than throw, so tests exercise their real nodes, not the synthetic root.
    /// </summary>
    public const string RootActivityVersionId = "$workflow-root";

    /// <summary>
    /// Wraps a catalog in the scoped, memoizing <see cref="CatalogVersionResolver"/> the
    /// catalog-consulting validators depend on (distinct from the argless <see cref="Resolver()"/>,
    /// which builds a <see cref="ScopedVariableResolver"/>).
    /// </summary>
    public static CatalogVersionResolver CatalogResolver(Elsa.Activities.Design.Core.Contracts.IActivityDefinitionLookup catalog) => new(catalog);

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

    public static ScopedVariableResolver Resolver() =>
        new(new DefaultActivityStructureService([new TestActivityStructureHandler()]));

    public static ScopedVariablePicker Picker() => new(Resolver());

    public static IActivityStructureService StructureService() =>
        new DefaultActivityStructureService([new TestActivityStructureHandler()]);

    public static ScopedVariableReferenceRemapper Remapper() =>
        new(new DefaultActivityStructureService([new TestActivityStructureHandler()]));

    public static ScopedVariableAuthoringContract Authoring()
    {
        var resolver = Resolver();
        return new(resolver, new ScopedVariablePicker(resolver));
    }

    public static ActivityNode Node(
        string nodeId,
        string activityVersionId = "av-1",
        IEnumerable<ArgumentState>? inputs = null,
        IEnumerable<ArgumentState>? outputs = null,
        bool isStart = false,
        IEnumerable<ActivityNode>? childActivities = null,
        IEnumerable<VariableDefinition>? containerVariables = null
    ) => new(
        NodeId: nodeId,
        ActivityVersionId: activityVersionId,
        Inputs: inputs ?? [],
        Outputs: outputs ?? [],
        Structure: childActivities is null && containerVariables is null
            ? null
            : TestActivityStructureHandler.CreateStructure(
                childActivities ?? [],
                (childActivities ?? []).FirstOrDefault()?.NodeId,
                containerVariables?.ToArray())
    );

    public static VariableDefinition Variable(string referenceKey, string name) => new(
        ReferenceKey: referenceKey,
        Name: name,
        Type: new Primitives.Models.TypeReference("String"),
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
            ActivityVersionId: RootActivityVersionId,
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
