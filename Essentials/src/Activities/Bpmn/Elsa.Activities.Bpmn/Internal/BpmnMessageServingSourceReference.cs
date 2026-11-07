using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Data projection of the existing trusted authored publisher's immutable provenance capture.</summary>
/// <remarks>Neither this factory nor a label DTO authorizes publication. The existing authoring owner performs
/// principal/tenant/version/definition/permission checks before producing that capture. Runtime serving authority
/// is subsequently established by the actual fenced source owner and canonical activation slot, never by these labels.</remarks>
internal static class BpmnMessageServingSourceReference
{
    internal const string SourceKind = "WorkflowDefinitionVersion";
    internal static WorkflowExecutableSourceReference Create(BpmnModelPublicationProvenance authored, string artifactId,
        string activationId, string slotName, string? tenantId, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(authored);
        var definitionId = authored.DefinitionId ?? throw new NotSupportedException("Canonical authored WorkflowDefinition provenance is required.");
        return new(WorkflowActivationReferenceIdentity.Create(activationId), artifactId, SourceKind,
            authored.VersionId, authored.Version, definitionId, authored.VersionId, authored.Version, createdAt, createdAt,
            WorkflowExecutableReferenceScope.Published, Layout: authored.Layout, ActivationId: activationId,
            SlotId: WorkflowActivationSlotIdentity.Create(definitionId, slotName), TenantId: tenantId,
            ActivityPresentation: authored.ActivityPresentation);
    }
}
