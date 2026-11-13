using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Bpmn.Core;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Pure transient scope projection; no mutable parent context or lookup service.</summary>
internal sealed class BpmnDocumentScopeViewHandler : IActivityStructureHandler
{
    public string Kind => BpmnDocumentScopeView.Kind;
    public string SchemaVersion => BpmnDocumentScopeView.SchemaVersion;
    public bool SupportsScopedVariables => true;

    // The actual source scope, rather than the derived view, must establish occurrence semantics.
    public ActivityChildResultFacts AssessChildResult(ActivityNode activity, string producerChildNodeId, string consumerChildNodeId) =>
        Analysis(activity)?.Assess(producerChildNodeId, consumerChildNodeId)
        ?? new(ActivityChildResultAvailability.Unknown) { EarlierProducerRunCanReachConsumer = null };

    public IReadOnlyList<string>? ChildrenCompletedBefore(ActivityNode activity, string childNodeId) => Analysis(activity)?.CompletedBefore(childNodeId);

    private static BpmnLinearChildResultAnalysis? Analysis(ActivityNode activity)
    {
        var view = BpmnDocumentScopeProjection.ReadView(activity);
        return BpmnDocumentChildResultAnalysis.TryCreate(view.Owner, view.ScopeElementId);
    }

    /// <exception cref="BpmnDocumentSourceException">Malformed source retains its parser cause and node/scope identity.</exception>
    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var view = BpmnDocumentScopeProjection.ReadView(activity);
        return BpmnDocumentScopeProjection.Project(view.Owner, view.ScopeElementId);
    }

    /// <exception cref="BpmnDocumentSourceException">Malformed source retains its parser cause and node/scope identity.</exception>
    public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) =>
        BpmnDocumentScopeProjection.QueryVariables(activity);

    /// <exception cref="BpmnDocumentSourceException">Malformed source retains its parser cause and node/scope identity.</exception>
    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) =>
        BpmnDocumentScopeProjection.ReplaceView(activity, childProjections);

    /// <exception cref="BpmnDocumentSourceException">Malformed source retains its parser cause and node/scope identity.</exception>
    /// <exception cref="NotSupportedException">Complete-source execution remains closed.</exception>
    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        BpmnDocumentScopeProjection.ReadView(activity);
        throw new NotSupportedException("Complete-source BPMN scope compilation is not qualified. Derived scope views are query-only and never physically authored source.");
    }

    /// <exception cref="BpmnDocumentSourceException">Malformed source retains its original cause and owning identifiers.</exception>
    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity, ActivityStructureCompilationContext context)
    {
        var view = BpmnDocumentScopeProjection.ReadView(activity);
        if (view.ScopeElementId == BpmnDocumentStructureHandler.Read(view.Owner).ProcessId)
            throw new NotSupportedException("A process query view cannot become physical compilation source.");
        var header = BpmnDocumentStructureHandler.Read(view.Owner).ScopeHeaders.Single(item => item.ScopeElementId == view.ScopeElementId);
        if (activity.NodeId != header.NodeId || activity.ActivityVersionId != header.ActivityVersionId ||
            activity.ExecutionPlacement != header.ExecutionPlacement || !Same(activity.Inputs, header.Inputs) ||
            !Same(activity.Outputs, header.Outputs) || !Same(view.Variables, header.Variables))
            throw new ArgumentException("A BPMN scope invocation must retain its physical canonical header configuration.");
        return BpmnDocumentResolvedCompilation.Compile(view.Owner, view.ScopeElementId, context);
    }

    private static bool Same<T>(T left, T right) => System.Text.Json.JsonElement.DeepEquals(
        System.Text.Json.JsonSerializer.SerializeToElement(left, BpmnDocumentStructureHandler.SerializerOptions),
        System.Text.Json.JsonSerializer.SerializeToElement(right, BpmnDocumentStructureHandler.SerializerOptions));
}
