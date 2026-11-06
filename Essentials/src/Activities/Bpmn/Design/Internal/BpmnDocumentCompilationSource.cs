using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>
/// Uses the existing post-compilation fan-in to verify catalog-resolved scope and Decision types.
/// Full-source syntax is admitted separately before every BPMN lowering, including nested invocations.
/// </summary>
internal sealed class BpmnDocumentCompilationSource(
    IEnumerable<IActivityStructureHandler> handlers,
    IEnumerable<IExecutableStructureNormalizer>? normalizers = null) : IExecutableCompilationSource
{
    private readonly DefaultActivityStructureService _structures = new(handlers);
    private readonly IExecutableStructureNormalizer[] _normalizers = (normalizers ?? []).ToArray();

    public ValueTask<ExecutableCompilationContribution> GetContributionAsync(ExecutableCompilationContext context, CancellationToken cancellationToken = default)
    {
        var compiled = context.RootActivity.DescendantsAndSelf().ToDictionary(node => node.ExecutableNodeId, StringComparer.Ordinal);
        var pending = new Stack<ActivityNode>();
        if (context.Source.State.RootActivity is { } root) pending.Push(root);
        while (pending.TryPop(out var activity))
        {
            cancellationToken.ThrowIfCancellationRequested();
            BpmnDocumentScopeProjection.RejectPhysicalView(activity);
            if (activity.Structure is not { Kind: BpmnActivityIdentities.StructureKind, SchemaVersion: BpmnAuthoredDocument.SchemaVersion })
            {
                foreach (var child in _structures.ProjectChildren(activity).SelectMany(slot => slot.Activities)) pending.Push(child);
                continue;
            }
            var source = BpmnDocumentStructureHandler.Read(activity);
            BpmnDocumentAdmission.Require(activity);
            VerifyScope(activity.NodeId, source.ProcessId);
            foreach (var header in source.ScopeHeaders) VerifyScope(header.NodeId, header.ScopeElementId);
            var xml = BpmnDocumentStructureHandler.ParseXml(source.DocumentXml);
            foreach (var binding in source.Bindings.Where(binding => binding.ChildNodeId is not null))
            {
                var element = xml.Descendants().Single(element => element.Name.NamespaceName == BpmnDocumentStructureHandler.ModelNamespace && (string?)element.Attribute("id") == binding.ElementId);
                if ((element.Name.LocalName is "exclusiveGateway" or "inclusiveGateway") &&
                    (!compiled.TryGetValue(binding.ChildNodeId!, out var decision) || decision.ActivityType != BpmnActivityIdentities.DecisionType))
                    throw new ArgumentException($"BPMN source '{activity.NodeId}', gateway '{binding.ElementId}' requires the exact native Decision activity type.");
            }
            foreach (var child in source.Activities) pending.Push(child);

            void VerifyScope(string nodeId, string scopeId)
            {
                if (!compiled.TryGetValue(nodeId, out var node) || node.AuthoredActivityId != nodeId || node.ActivityType != BpmnActivityIdentities.ProcessType ||
                    node.Structure is not { Kind: BpmnActivityIdentities.StructureKind, SchemaVersion: BpmnActivityIdentities.StructureSchemaVersion } structure)
                    throw new ArgumentException($"BPMN source '{activity.NodeId}', scope '{scopeId}' requires an authorable exact BpmnProcess invocation; a different activity or placed template cannot replace its source.");
                if (_normalizers.Length != 1)
                    throw new InvalidOperationException("Complete-source BPMN verification requires exactly one canonical executable structure normalizer.");
                var expected = _normalizers[0].Normalize(nodeId, BpmnDocumentAdmission.Lower(activity, scopeId))
                    ?? throw new InvalidOperationException("The canonical executable structure normalizer returned no BPMN structure.");
                if (expected.Kind != structure.Kind || expected.SchemaVersion != structure.SchemaVersion ||
                    !JsonElement.DeepEquals(expected.Payload, structure.Payload))
                    throw new ArgumentException($"BPMN source '{activity.NodeId}', scope '{scopeId}' did not retain its complete admitted semantic payload during compilation.");
            }
        }
        return ValueTask.FromResult(new ExecutableCompilationContribution());
    }

}
