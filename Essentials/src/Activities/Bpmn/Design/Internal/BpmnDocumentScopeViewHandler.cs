using Elsa.Activities.Bpmn.Models;
using System.Text.Json;
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

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        var view = BpmnDocumentScopeProjection.ReadView(activity);
        BpmnDocumentAdmission.Require(view.Owner);
        throw new NotSupportedException("Complete-source BPMN scope lowering remains on hold until every real compile route qualifies resolved catalog types and complete semantics. A derived scope view is never a physically authorable source.");
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity, ActivityStructureCompilationContext context)
    {
        var view = BpmnDocumentScopeProjection.ReadView(activity);
        var header = BpmnDocumentStructureHandler.Read(view.Owner).ScopeHeaders.Single(item => item.ScopeElementId == view.ScopeElementId);
        if (activity.NodeId != header.NodeId || activity.ActivityVersionId != header.ActivityVersionId ||
            activity.ExecutionPlacement != header.ExecutionPlacement || !Same(activity.Inputs, header.Inputs) ||
            !Same(activity.Outputs, header.Outputs) || !Same(view.Variables, header.Variables))
            throw new ArgumentException("A derived BPMN scope invocation must retain its physical canonical header configuration.");
        return BpmnDocumentResolvedCompilation.Compile(view.Owner, view.ScopeElementId, context);
    }

    private static bool Same<T>(T left, T right) => JsonElement.DeepEquals(
        JsonSerializer.SerializeToElement(left, BpmnDocumentStructureHandler.SerializerOptions),
        JsonSerializer.SerializeToElement(right, BpmnDocumentStructureHandler.SerializerOptions));
}
