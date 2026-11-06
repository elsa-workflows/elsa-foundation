using System.Text.Json;
using System.Xml.Linq;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Complete-source admission and exact resolved native roles before derived schema1 lowering.</summary>
internal static class BpmnDocumentResolvedCompilation
{
    internal static ActivityNodeStructure Compile(ActivityNode owner, string scopeId, ActivityStructureCompilationContext context)
    {
        BpmnDocumentAdmission.Require(owner);
        RequirePhysicalOwner(owner, context);
        var source = BpmnDocumentStructureHandler.Read(owner);
        RequireInlinePlacement(owner, context);
        RequireRole(owner.NodeId, owner.ActivityVersionId, BpmnActivityIdentities.ProcessType, requireStructure: true);
        foreach (var header in source.ScopeHeaders)
            RequireRole(header.NodeId, header.ActivityVersionId, BpmnActivityIdentities.ProcessType, requireStructure: true);
        var xml = BpmnDocumentStructureHandler.ParseXml(source.DocumentXml);
        XNamespace model = BpmnDocumentStructureHandler.ModelNamespace;
        var children = source.Activities.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        foreach (var binding in source.Bindings.Where(binding => binding.ChildNodeId is not null))
        {
            var element = xml.Descendants().Single(element => element.Name.Namespace == model && (string?)element.Attribute("id") == binding.ElementId);
            if (element.Name.LocalName is "exclusiveGateway" or "inclusiveGateway")
                RequireRole(binding.ChildNodeId!, children[binding.ChildNodeId!].ActivityVersionId,
                    BpmnActivityIdentities.DecisionType, requireStructure: false);
        }
        var lowered = BpmnDocumentAdmission.Lower(owner, scopeId);
        var scopeNodeId = scopeId == source.ProcessId ? owner.NodeId : source.ScopeHeaders.Single(header => header.ScopeElementId == scopeId).NodeId;
        var declared = context.Invocations[scopeNodeId].DeclaredStructure!;
        if (lowered.Kind != declared.Kind || lowered.SchemaVersion != declared.SchemaVersion)
            throw new ArgumentException($"BPMN source '{owner.NodeId}', scope '{scopeId}' did not lower to its exact declared runtime structure contract.");
        return lowered;

        void RequireRole(string nodeId, string versionId, string runtimeType, bool requireStructure)
        {
            if (!context.Invocations.TryGetValue(nodeId, out var fact) || fact.ActivityVersionId != versionId ||
                fact.ResolutionKind != ActivityDefinitionVersionResolutionKind.AuthorableActivity ||
                fact.RuntimeActivityTypeKey != runtimeType || fact.RuntimeConsumerKey != WellKnownRuntimeActivityConsumers.ClrActivity ||
                fact.RuntimeDescriptorSchemaVersion != RuntimeActivityDescriptor.InitialSchemaVersion ||
                requireStructure && fact.DeclaredStructure is not { Kind: BpmnActivityIdentities.StructureKind, SchemaVersion: BpmnActivityIdentities.StructureSchemaVersion })
                throw new ArgumentException($"BPMN source '{owner.NodeId}', native invocation '{nodeId}' requires its exact authorable native runtime role '{runtimeType}'.");
        }
    }

    private static void RequireInlinePlacement(ActivityNode owner, ActivityStructureCompilationContext context)
    {
        // Graph occurrence overlays do not yet carry execution placement. Refuse this intent
        // before lowering rather than allowing an overlay to silently turn it into inline work.
        var pending = new Stack<ActivityNode>();
        pending.Push(owner);
        while (pending.TryPop(out var node))
        {
            if (node.ExecutionPlacement != ActivityExecutionPlacement.Inline)
                throw new NotSupportedException($"BPMN source '{owner.NodeId}', native invocation '{node.NodeId}' has unqualified background execution placement.");
            if (node.Structure is { Kind: BpmnActivityIdentities.StructureKind, SchemaVersion: BpmnAuthoredDocument.SchemaVersion })
            {
                var document = BpmnDocumentStructureHandler.Read(node);
                foreach (var header in document.ScopeHeaders)
                    if (header.ExecutionPlacement != ActivityExecutionPlacement.Inline)
                        throw new NotSupportedException($"BPMN source '{owner.NodeId}', scope '{header.ScopeElementId}' has unqualified background execution placement.");
                foreach (var child in document.Activities) pending.Push(child);
            }
            else
                foreach (var child in context.ChildProjections[node.NodeId].SelectMany(slot => slot.Activities)) pending.Push(child);
        }
    }

    internal static void RequirePhysicalOwner(ActivityNode owner, ActivityStructureCompilationContext context)
    {
        var pending = new Stack<ActivityNode>();
        var ownedIds = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(context.PhysicalRoot);
        var found = false;
        while (pending.TryPop(out var activity))
        {
            BpmnDocumentScopeProjection.RejectPhysicalView(activity);
            if (!ownedIds.Add(activity.NodeId)) throw new ArgumentException("Physical source contains repeated native ownership identities.");
            if (activity.NodeId == owner.NodeId)
            {
                if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(activity, BpmnDocumentStructureHandler.SerializerOptions),
                        JsonSerializer.SerializeToElement(owner, BpmnDocumentStructureHandler.SerializerOptions)))
                    throw new ArgumentException($"BPMN source '{owner.NodeId}' does not match its physical canonical owner.");
                found = true;
            }
            IEnumerable<ActivityNode> children;
            if (activity.Structure is { Kind: BpmnActivityIdentities.StructureKind, SchemaVersion: BpmnAuthoredDocument.SchemaVersion })
            {
                var source = BpmnDocumentStructureHandler.Read(activity);
                foreach (var header in source.ScopeHeaders)
                    if (!ownedIds.Add(header.NodeId)) throw new ArgumentException("Physical source contains repeated native scope identities.");
                children = source.Activities;
            }
            else if (context.ChildProjections.TryGetValue(activity.NodeId, out var projection))
                children = projection.SelectMany(slot => slot.Activities);
            else throw new ArgumentException($"Physical activity '{activity.NodeId}' has no captured ownership projection.");
            foreach (var child in children) pending.Push(child);
        }
        if (!found) throw new ArgumentException($"BPMN source '{owner.NodeId}' is not a physical owner in this compilation source.");
    }
}
