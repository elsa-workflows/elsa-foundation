using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Core;

/// <summary>
/// Immutable authored provenance captured by a trusted publisher, separate from executable model content.
/// Runtime publication and recovery do not read the Design store.
/// </summary>
public sealed class BpmnModelPublicationProvenance
{
    private string? definitionId;

    [JsonConstructor]
    public BpmnModelPublicationProvenance(string versionId, string version,
        IReadOnlyList<WorkflowExecutableLayoutRecord>? layout = null,
        IReadOnlyList<WorkflowExecutableActivityPresentationRecord>? activityPresentation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (layout?.Any(record => record is null) == true || activityPresentation?.Any(record => record is null) == true)
            throw new ArgumentException("Publication presentation cannot contain null records.");
        VersionId = versionId;
        Version = version;
        Layout = Array.AsReadOnly((layout ?? []).Select(record => record with
            { AdditionalProperties = record.AdditionalProperties?.Clone() }).ToArray());
        ActivityPresentation = Array.AsReadOnly((activityPresentation ?? []).ToArray());
    }

    /// <summary>Captures trusted canonical authored identity without changing executable content identity.</summary>
    /// <remarks>The ID is opaque and must originate from the owning authored definition, never a model material hash.</remarks>
    public static BpmnModelPublicationProvenance ForAuthoredDefinition(string definitionId, string versionId, string version,
        IReadOnlyList<WorkflowExecutableLayoutRecord>? layout = null,
        IReadOnlyList<WorkflowExecutableActivityPresentationRecord>? activityPresentation = null)
    {
        if (!BpmnModelSourceProvenance.IsValidDefinitionId(definitionId))
            throw new ArgumentException("Authored BPMN definition provenance must be a nonblank identity.", nameof(definitionId));
        return new(versionId, version, layout, activityPresentation) { DefinitionId = definitionId };
    }

    public string VersionId { get; }
    public string Version { get; }
    /// <summary>Canonical ID in the model-neutral WorkflowDefinition namespace, absent for legacy/standalone sources.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefinitionId
    {
        get => definitionId;
        init
        {
            if (value is not null && !BpmnModelSourceProvenance.IsValidDefinitionId(value))
                throw new ArgumentException("Authored BPMN definition provenance must be a nonblank identity.", nameof(DefinitionId));
            definitionId = value;
        }
    }
    public IReadOnlyList<WorkflowExecutableLayoutRecord> Layout { get; }
    public IReadOnlyList<WorkflowExecutableActivityPresentationRecord> ActivityPresentation { get; }
}

/// <summary>BPMN-owned source encoding; source attribution remains separate from the unchanged material pin.</summary>
internal static class BpmnModelSourceProvenance
{
    internal const string LegacyKind = "BpmnModel";
    internal const string AuthoredKind = "BpmnModel.Authored.v1";

    internal static bool IsSupported(WorkflowExecutableSourceReference source) =>
        source.SourceKind is LegacyKind or AuthoredKind;

    internal static bool MatchesMaterial(WorkflowExecutableSourceReference source, WorkflowExecutableIdentity material) =>
        IsSupported(source) && source.ArtifactId == material.ArtifactId &&
        source.DefinitionVersionId == material.DefinitionVersionId && source.ArtifactVersion == material.ArtifactVersion &&
        (source.SourceKind == LegacyKind ? source.DefinitionId == material.DefinitionId :
            source.Scope == WorkflowExecutableReferenceScope.Published && IsValidDefinitionId(source.DefinitionId) &&
            !string.IsNullOrWhiteSpace(source.SourceId) && !string.IsNullOrWhiteSpace(source.SourceVersion));

    internal static string? ReadDefinitionId(WorkflowExecutableSourceReference source) =>
        source.SourceKind == AuthoredKind && IsValidDefinitionId(source.DefinitionId) ? source.DefinitionId : null;

    internal static bool IsValidDefinitionId(string? value) => !string.IsNullOrWhiteSpace(value);
}
