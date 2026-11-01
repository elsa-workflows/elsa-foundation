using System.Text.Json;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Activities.Bpmn.Internal;

internal sealed class BpmnEmbeddingPinSource(IWorkflowExecutableStore materials, IWorkflowExecutableSourceReferenceStore sources,
    TimeProvider clock) : IExecutableCompilationSource
{
    public async ValueTask<ExecutableCompilationContribution> GetContributionAsync(ExecutableCompilationContext context, CancellationToken cancellationToken = default)
    {
        var metadata = new List<ExecutableNodeMetadataClaim>();
        var dependencies = new List<ExecutableDependencyClaim>();
        foreach (var node in context.RootActivity.DescendantsAndSelf().Where(node => node.ActivityType == RunBpmn.ActivityType))
        {
            var binding = node.InputBindings.GetValueOrDefault(nameof(RunBpmn.Model));
            SecretBindingDiagnostics.ThrowIfSecretRead(binding, node.ExecutableNodeId, nameof(RunBpmn.Model));
            if (binding is not { Source: RuntimeInputBindingSource.Literal, LiteralValue: { ValueKind: JsonValueKind.String } literal } ||
                string.IsNullOrWhiteSpace(literal.GetString()))
                throw Refused(node, "requires a literal published BPMN model source reference.");
            var source = await sources.FindAsync(literal.GetString()!, cancellationToken);
            if (source is null || source.TenantId != context.TenantId || source.SourceKind != "BpmnModel" || !source.IsLive(clock.GetUtcNow()))
                throw Refused(node, "requires an accessible live published BPMN model source.");
            var material = await materials.FindMaterialAsync(source.ArtifactId, cancellationToken);
            if (material?.Model is not { Profile: BpmnModelProtocol.Profile or BpmnModelProtocol.NativeWorkProfile } model ||
                model.Protocol != new RuntimeExecutionProtocol(BpmnModelProtocol.Key, BpmnModelProtocol.Version) || material.Identity.DefinitionId != source.DefinitionId ||
                material.Identity.DefinitionVersionId != source.DefinitionVersionId || material.Identity.ArtifactVersion != source.ArtifactVersion)
                throw Refused(node, "requires a qualified published BPMN human-work or native-service profile.");
            var pin = new BpmnEmbeddingPin(source.SourceReferenceId, material.Identity, material.IncarnationId);
            metadata.Add(new(node.ExecutableNodeId, BpmnEmbeddingBoundary.PinKey, BpmnEmbeddingJson.Serialize(pin)));
            dependencies.Add(new(node.ExecutableNodeId, material.Identity.ArtifactId, material.Identity.ArtifactHash));
        }
        return new(metadata, dependencies);
    }

    private static ArgumentException Refused(ExecutableNode node, string message) => new($"RunBpmn node '{node.ExecutableNodeId}' {message}");
}
