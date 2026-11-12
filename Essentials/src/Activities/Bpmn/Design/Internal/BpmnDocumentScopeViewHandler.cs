using Elsa.Activities.Bpmn.Models;
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

    // A derived view does not establish execution or occurrence semantics for its native children.
    public ActivityChildResultFacts AssessChildResult(ActivityNode activity, string producerChildNodeId, string consumerChildNodeId) =>
        new(ActivityChildResultAvailability.Unknown) { EarlierProducerRunCanReachConsumer = null };

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var view = BpmnDocumentScopeProjection.ReadView(activity);
        return BpmnDocumentScopeProjection.Project(view.Owner, view.ScopeElementId);
    }

    public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) =>
        BpmnDocumentScopeProjection.QueryVariables(activity);

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) =>
        BpmnDocumentScopeProjection.ReplaceView(activity, childProjections);

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        BpmnDocumentScopeProjection.ReadView(activity);
        throw new NotSupportedException("Complete-source BPMN scope compilation is not qualified. Derived scope views are query-only and never physically authored source.");
    }
}
