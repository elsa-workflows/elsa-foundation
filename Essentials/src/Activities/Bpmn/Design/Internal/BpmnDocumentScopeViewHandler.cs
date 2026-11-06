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

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var view = BpmnDocumentScopeProjection.ReadView(activity);
        return BpmnDocumentScopeProjection.Project(view.Owner, view.ScopeElementId);
    }

    public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) =>
        BpmnDocumentScopeProjection.ReadView(activity).Variables;

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) =>
        BpmnDocumentScopeProjection.ReplaceView(activity, childProjections);

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity) =>
        throw new NotSupportedException("A derived BPMN scope view is not an executable or physically authorable structure. Complete-content admission and lowering are not qualified (#2736).");
}
